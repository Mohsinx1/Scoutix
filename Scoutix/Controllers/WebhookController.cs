using Azure.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Scoutix.Models;
using Scoutix.Services.EmailService;
using System.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace Scoutix.Controllers
{
    [AllowAnonymous]
    public class WebhookController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PasswordHasher<User> _passwordHasher;
        private readonly IConfiguration _configuration;
        private readonly ILogger<WebhookController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IEmailService _emailService;

        public WebhookController(ApplicationDbContext context, IConfiguration configuration,
            ILogger<WebhookController> logger, IHttpClientFactory httpClientFactory, IEmailService emailService)
        {
            _context = context;
            _passwordHasher = new PasswordHasher<User>();
            _configuration = configuration;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _emailService = emailService;
        }


        [HttpPost]
        [Route("paddle-webhook")]
        [DisableRequestSizeLimit]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> PaddleWebhook()
        {
            WebhookLog webhookLog = null;
            try
            {
                Request.EnableBuffering();
               
                string notificationJson;
                using (var reader = new StreamReader(Request.Body, leaveOpen: true))
                {
                    notificationJson = await reader.ReadToEndAsync();
                }

                if (string.IsNullOrEmpty(notificationJson))
                {
                    _logger.LogWarning("Paddle webhook received empty body");
                    return Ok();
                }
                _logger.LogInformation("Paddle webhook received: {json}", notificationJson);

                // ── Paddle Signature Validation ──────────────────────────────
                var paddleSignature = Request.Headers["Paddle-Signature"].ToString();
                if (string.IsNullOrEmpty(paddleSignature))
                {
                    _logger.LogWarning("Paddle webhook missing signature header");
                    return Unauthorized();
                }

                var webhookSecret = _configuration["Paddle:WebhookSecret"];

                // Header format: ts=1234567890;h1=abcdef...
                var parts = paddleSignature.Split(';');
                var ts = parts.FirstOrDefault(p => p.StartsWith("ts="))?.Substring(3);
                var h1 = parts.FirstOrDefault(p => p.StartsWith("h1="))?.Substring(3);

                if (string.IsNullOrEmpty(ts) || string.IsNullOrEmpty(h1))
                {
                    _logger.LogWarning("Paddle webhook malformed signature header");
                    return Unauthorized();
                }

                var signedPayload = $"{ts}:{notificationJson}";
                var secretBytes = Encoding.UTF8.GetBytes(webhookSecret);
                var payloadBytes = Encoding.UTF8.GetBytes(signedPayload);

                using var hmac = new HMACSHA256(secretBytes);
                var computedHash = hmac.ComputeHash(payloadBytes);
                var computedHex = Convert.ToHexString(computedHash).ToLower();

                if (!CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(computedHex),
                        Encoding.UTF8.GetBytes(h1)))
                {
                    _logger.LogWarning("Paddle webhook signature mismatch");
                    return Unauthorized();
                }
                // ── End Signature Validation ─────────────────────────────────

                dynamic notification = JsonConvert.DeserializeObject<dynamic>(notificationJson);
                string eventType = notification.event_type;
                string eventId = notification.event_id?.ToString();

                // Idempotency check
                var existingLog = await _context.WebhookLogs
                    .FirstOrDefaultAsync(w => w.PaddleEventId == eventId);

                if (existingLog != null)
                {
                    if (existingLog.ProcessedSuccessfully == true)
                    {
                        _logger.LogInformation("Duplicate webhook ignored: {EventId}", eventId);
                        return Ok();
                    }

                    // Previous attempt failed — reuse the existing log row and retry
                    _logger.LogInformation("Retrying previously failed webhook: {EventId}", eventId);
                    webhookLog = existingLog;
                    webhookLog.ErrorMessage = null;
                    webhookLog.ReceivedAt = DateTime.UtcNow;
                }
                else
                {
                    webhookLog = new WebhookLog
                    {
                        PaddleEventId = eventId,
                        EventType = eventType,
                        Payload = notificationJson,
                        ReceivedAt = DateTime.UtcNow,
                        ProcessedSuccessfully = false
                    };
                    _context.WebhookLogs.Add(webhookLog);

                    try
                    {
                        await _context.SaveChangesAsync();
                    }
                    catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UX_WebhookLogs_PaddleEventId") == true)
                    {
                        _logger.LogInformation("Race condition duplicate webhook ignored: {EventId}", eventId);
                        return Ok();
                    }
                }

                _logger.LogInformation("Paddle event received: {EventType} ({EventId})", eventType, eventId);

                switch (eventType)
                {
                    case "subscription.activated":
                        await HandleSubscriptionActivated(notification);
                        break;
                    case "subscription.updated":
                        await HandleSubscriptionUpdated(notification);
                        break;
                    case "subscription.canceled":
                        await HandleSubscriptionCanceled(notification);
                        break;
                    case "subscription.past_due":
                        await HandleSubscriptionPastDue(notification);
                        break;
                    case "transaction.completed":
                        await HandleTransactionCompleted(notification);
                        break;
                    case "transaction.payment_failed":
                        await HandlePaymentFailed(notification);
                        break;
                    default:
                        _logger.LogInformation("Ignored Paddle event: {EventType}", eventType);
                        break;
                }

                webhookLog.ProcessedSuccessfully = true;
                await _context.SaveChangesAsync();

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling Paddle webhook");
                if (webhookLog != null)
                {
                    webhookLog.ErrorMessage = ex.Message;
                    await _context.SaveChangesAsync();
                }
                return StatusCode(500, "Internal server error");
            }
        }

        // ─────────────────────────────────────────────
        // HELPER: Extract email from custom_data
        // This is the email you passed during Paddle.Checkout.open()
        // ─────────────────────────────────────────────
        private string GetEmailFromCustomData(dynamic data)
        {
            try
            {
                string email = data.custom_data?.email?.ToString();
                if (string.IsNullOrEmpty(email))
                    _logger.LogWarning("custom_data.email is missing from Paddle event");
                _logger.LogInformation("custom_data.email: {email}", email);
                return email;
            }
            catch
            {
                _logger.LogWarning("Could not read custom_data.email from Paddle event");
                return null;
            }
        }

        // ─────────────────────────────────────────────
        // HELPER: Extract plan name from subscription items
        // ─────────────────────────────────────────────
        private string GetPlanName(dynamic data)
        {
            try
            {
                // items[0].price.name e.g. "Pro plan monthly subscription"
                return data.items[0]?.price?.name?.ToString();
            }
            catch
            {
                return "Unknown";
            }
        }

        // ─────────────────────────────────────────────
        // 1. subscription.activated
        // Fires when a user's first payment succeeds — GRANT ACCESS
        // ─────────────────────────────────────────────
        private async Task HandleSubscriptionActivated(dynamic notification)
        {
            var data = notification.data;
            string email = GetEmailFromCustomData(data);
            string paddleSubscriptionId = data.id?.ToString();
            string planName = GetPlanName(data);
            string status = data.status?.ToString();
            string priceId = data.items[0]?.price?.id?.ToString();
            DateTime startDate = DateTime.Parse(data.current_billing_period?.starts_at?.ToString());
            DateTime endDate = DateTime.Parse(data.current_billing_period?.ends_at?.ToString());
            string paddleCustomerId = data.customer_id?.ToString();

            _logger.LogInformation("Subscription activated for {Email}, plan: {Plan}, subId: {SubId}",
                email, planName, paddleSubscriptionId);

            if (string.IsNullOrEmpty(email)) return;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                _logger.LogWarning("subscription.activated — user not found for email: {Email}", email);
                return;
            }

            var plan = await _context.Plans.FirstOrDefaultAsync(p => p.PaddlePriceId == priceId);
            if (plan == null)
            {
                _logger.LogWarning("subscription.activated — plan not found for priceId: {PriceId}", priceId);
                return;
            }

            // Find temp row created by CheckoutSuccess (no PaddleSubscriptionId yet)
            var subscription = await _context.Subscriptions
                .FirstOrDefaultAsync(s => s.UserId == user.Id && s.PaddleSubscriptionId == null);

            if (subscription == null)
            {
                _logger.LogWarning("subscription.activated — no temp row found for {Email}, triggering retry", email);
                throw new Exception($"Temp subscription row not found for {email} — CheckoutSuccess may not have run yet");
            }

            subscription.PaddleSubscriptionId = paddleSubscriptionId;
            subscription.PlanId = plan.Id;
            subscription.IsSubscriptionActive = true;
            subscription.SubscriptionStatus = "active";
            subscription.StartDate = startDate;
            subscription.EndDate = endDate;
            subscription.UpdatedAt = DateTime.UtcNow;

            user.PaddleCustomerId = paddleCustomerId;
            user.SubscriptionId = subscription.Id;
            user.PlanId = plan.Id;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Subscription activated successfully for {Email}", email);
        }

        // ─────────────────────────────────────────────
        // 2. subscription.updated
        // Fires on upgrade, downgrade, or quantity change
        // ─────────────────────────────────────────────
        private async Task HandleSubscriptionUpdated(dynamic notification)
        {
            var data = notification.data;
            string paddleSubscriptionId = data.id?.ToString();
            string status = data.status?.ToString();
            string newPriceId = data.items[0].price.id?.ToString();
            string newPlanName = data.items[0].product.name?.ToString();

            _logger.LogInformation("Subscription updated — SubId: {SubId}, new plan: {Plan}, status: {Status}",
                paddleSubscriptionId, newPlanName, status);

            if (string.IsNullOrEmpty(paddleSubscriptionId)) return;

            // Look up subscription directly by PaddleSubscriptionId
            var subscription = await _context.Subscriptions
                .FirstOrDefaultAsync(s => s.PaddleSubscriptionId == paddleSubscriptionId);

            if (subscription == null)
            {
                _logger.LogWarning("HandleSubscriptionUpdated: No subscription found for {PaddleSubscriptionId}", paddleSubscriptionId);
                return;
            }

            // Get user from subscription
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == subscription.UserId);
            if (user == null)
            {
                _logger.LogWarning("HandleSubscriptionUpdated: No user found for subscription {PaddleSubscriptionId}", paddleSubscriptionId);
                return;
            }

            // Find new plan by PaddlePriceId
            var newPlan = await _context.Plans.FirstOrDefaultAsync(p => p.PaddlePriceId == newPriceId);
            if (newPlan == null)
            {
                _logger.LogWarning("HandleSubscriptionUpdated: No plan found for PriceId {PriceId}", newPriceId);
                return;
            }

            subscription.PlanId = newPlan.Id;
            subscription.SubscriptionStatus = status;
            subscription.UpdatedAt = DateTime.UtcNow;

            user.PlanId = newPlan.Id;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Plan updated to {PlanName} for userId {UserId}", newPlan.PlanName, user.Id);
        }

        // ─────────────────────────────────────────────
        // 3. subscription.canceled
        // User canceled — don't remove access immediately!
        // Let them use the app until current billing period ends
        // ─────────────────────────────────────────────
        private async Task HandleSubscriptionCanceled(dynamic notification)
        {
            var data = notification.data;
            string paddleSubscriptionId = data.id?.ToString();
            string paddleCustomerId = data.customer_id?.ToString();
            string canceledAtRaw = data.canceled_at?.ToString();

            DateTime? canceledAt = null;
            if (!string.IsNullOrEmpty(canceledAtRaw))
                canceledAt = DateTime.Parse(canceledAtRaw);

            _logger.LogInformation("Subscription canceled — SubId: {SubId}, CustomerId: {CustomerId}",
                paddleSubscriptionId, paddleCustomerId);

            if (string.IsNullOrEmpty(paddleSubscriptionId)) return;

            // Find subscription directly by PaddleSubscriptionId
            var subscription = await _context.Subscriptions
                .FirstOrDefaultAsync(s => s.PaddleSubscriptionId == paddleSubscriptionId);

            if (subscription == null)
            {
                _logger.LogWarning("subscription.canceled — no subscription found for subId: {SubId}", paddleSubscriptionId);
                return;
            }

            // Find user
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == subscription.UserId);
            if (user == null)
            {
                _logger.LogWarning("subscription.canceled — no user found for subscription: {SubId}", paddleSubscriptionId);
                return;
            }

            // Mark as canceled but keep IsSubscriptionActive = true
            // Access revocation happens via Hangfire daily job checking EndDate
            subscription.SubscriptionStatus = "canceled";
            subscription.CanceledAt = canceledAt;
            // EndDate stays as-is — user retains access until current period ends
            subscription.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Subscription marked as canceled for user {UserId}, access ends: {EndDate}",
                user.Id, subscription.EndDate);
        }

        private async Task HandleSubscriptionPastDue(dynamic notification)
        {
            var data = notification.data;
            string paddleSubscriptionId = data.id?.ToString();

            _logger.LogWarning("Subscription past due — SubId: {SubId}", paddleSubscriptionId);

            if (string.IsNullOrEmpty(paddleSubscriptionId)) return;

            var subscription = await _context.Subscriptions
                .FirstOrDefaultAsync(s => s.PaddleSubscriptionId == paddleSubscriptionId);

            if (subscription == null)
            {
                _logger.LogWarning("HandleSubscriptionPastDue: No subscription found for {PaddleSubscriptionId}", paddleSubscriptionId);
                return;
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == subscription.UserId);
            if (user == null)
            {
                _logger.LogWarning("HandleSubscriptionPastDue: No user found for subscription {PaddleSubscriptionId}", paddleSubscriptionId);
                return;
            }

            // Update status but keep IsSubscriptionActive = true
            // Don't revoke access yet — give user chance to update payment method
            subscription.SubscriptionStatus = "past_due";
            subscription.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Send warning email
            await _emailService.SendPastDueEmailAsync(user.Email, user.UserName);

            _logger.LogWarning("Subscription marked past_due for userId {UserId}", user.Id);
        }

        // ─────────────────────────────────────────────
        // 7. transaction.completed
        // Payment confirmed — good place to send receipt email
        // Note: Access is already granted via subscription.activated
        // ─────────────────────────────────────────────
        private async Task HandleTransactionCompleted(dynamic notification)
        {
            var data = notification.data;
            string email = GetEmailFromCustomData(data);
            string transactionId = data.id?.ToString();
            string currencyCode = data.currency_code?.ToString() ?? "USD";

            string amountRaw = data.details?.totals?.grand_total?.ToString() ?? "0";
            decimal amount = decimal.Parse(amountRaw) / 100;
            string amountFormatted = $"{currencyCode} {amount:F2}";

            string planName = data.items[0]?.price?.name?.ToString() ?? "Subscription";

            string billingStart = data.billing_period?.starts_at?.ToString();
            string billingEnd = data.billing_period?.ends_at?.ToString();
            string startFormatted = string.IsNullOrEmpty(billingStart) ? "—" : DateTime.Parse(billingStart).ToString("MMM dd, yyyy");
            string endFormatted = string.IsNullOrEmpty(billingEnd) ? "—" : DateTime.Parse(billingEnd).ToString("MMM dd, yyyy");

            string paymentDate = DateTime.UtcNow.ToString("MMM dd, yyyy");

            _logger.LogInformation("Transaction completed for {Email}, txn: {TxnId}, amount: {Amount}",
                email, transactionId, amountFormatted);

            if (string.IsNullOrEmpty(email)) return;

            await _emailService.SendReceiptEmailAsync(
                email, transactionId, amountFormatted, planName,
                paymentDate, startFormatted, endFormatted, endFormatted);
        }

        private async Task HandlePaymentFailed(dynamic notification)
        {
            var data = notification.data;
            string transactionId = data.id?.ToString();
            string paddleSubscriptionId = data.subscription_id?.ToString();
            string paddleCustomerId = data.customer_id?.ToString();
            string errorCode = data.payments?[0]?.error_code?.ToString();

            _logger.LogWarning("Payment failed — TxnId: {TxnId}, SubId: {SubId}, Error: {Error}",
                transactionId, paddleSubscriptionId, errorCode);

            if (string.IsNullOrEmpty(paddleSubscriptionId) && string.IsNullOrEmpty(paddleCustomerId))
            {
                _logger.LogWarning("HandlePaymentFailed: No subscription_id or customer_id in payload, skipping");
                return;
            }

            User user = null;

            // Try subscription_id first (most reliable)
            if (!string.IsNullOrEmpty(paddleSubscriptionId))
            {
                var subscription = await _context.Subscriptions
                    .FirstOrDefaultAsync(s => s.PaddleSubscriptionId == paddleSubscriptionId);

                if (subscription != null)
                    user = await _context.Users.FirstOrDefaultAsync(u => u.Id == subscription.UserId);
            }

            // Fall back to customer_id if subscription lookup failed
            if (user == null && !string.IsNullOrEmpty(paddleCustomerId))
            {
                user = await _context.Users.FirstOrDefaultAsync(u => u.PaddleCustomerId == paddleCustomerId);
            }

            if (user == null)
            {
                _logger.LogWarning("HandlePaymentFailed: No user found for TxnId {TxnId}", transactionId);
                return;
            }

            // Just send the email — don't change subscription status here
            // subscription.past_due handler will update status after all retries exhausted
            await _emailService.SendPaymentFailedEmailAsync(user.Email, user.UserName);

            _logger.LogWarning("Payment failed email sent to userId {UserId}, error: {Error}", user.Id, errorCode);
        }

    }
}

