using Hangfire;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Scoutix.Controllers;
using Scoutix.Models;
using Scoutix.Models.ViewModels;
using Scoutix.Services.EmailService;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

[AllowAnonymous]
public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly PasswordHasher<User> _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEmailService _emailService;

    public AccountController(ApplicationDbContext context, IConfiguration configuration,
        ILogger<AccountController> logger, IHttpClientFactory httpClientFactory, IEmailService emailService)
    {
        _context = context;
        _passwordHasher = new PasswordHasher<User>();
        _configuration = configuration;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _emailService = emailService;
    }

    [HttpGet]
    public IActionResult SignUp(int planId)
    {
        // Just pass the planId to the view in a hidden field
        var model = new UserViewModel
        {
            PlanId = planId // Store the planId in the model
        };

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> SignUp(UserViewModel model)
    {
        if (model == null)
        {
            TempData["Message"] = "The form data was not submitted properly. Please try again.";
            TempData["MessageType"] = "info";
            return View();
        }
        if (!model.AgreeToTerms)
        {
            TempData["Message"] = "Please Agree to Terms and Policy before proceeding.";
            TempData["MessageType"] = "warning";
            return View(model);
        }

        if (ModelState.IsValid)
        {
            try
            {
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == model.Email);

                if (existingUser != null)
                {
                    TempData["Message"] = "An account with this email already exists.";
                    TempData["MessageType"] = "warning";
                    TempData["ShowLoginLinks"] = true;
                    return View(model);
                }

                var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                var user = new User
                {
                    UserName = model.UserName,
                    Email = model.Email,
                    PasswordHash = _passwordHasher.HashPassword(new User(), model.Password),
                    AgreeToTerms = model.AgreeToTerms,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    VerificationToken = token,
                    PlanId = model.PlanId,
                    VerificationTokenExpiresAt = DateTime.UtcNow.AddHours(24)
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                var verificationUrl = Url.Action(
                    "VerifyEmail",
                    "Account",
                    new { token = token, planId = model.PlanId, email = model.Email },
                    Request.Scheme);
                await _emailService.SendVerificationEmailAsync(model.Email, verificationUrl);

                TempData["Message"] = "You have successfully signed up! Please check your email to verify your account.";
                TempData["MessageType"] = "success";

                return RedirectToAction("SignupConfirmation", "Account", new { email = model.Email, planId = model.PlanId });
            }
            catch (Exception ex)
            {
                TempData["Message"] = "An error occurred while processing your registration. Please try again.";
                TempData["MessageType"] = "error";
            }
        }
        else
        {
            TempData["Message"] = "Please fill in all the required fields.";
            TempData["MessageType"] = "warning";
        }

        return View(model);
    }
    [HttpGet]
    public async Task<IActionResult> VerifyEmail(string token, int planId, string email)
    {
        if (string.IsNullOrEmpty(token))
        {
            TempData["Message"] = "Invalid verification link.";
            TempData["MessageType"] = "error";
            return RedirectToAction("SignUp", "Account");
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.VerificationToken == token);

        if (user == null)
        {
            TempData["Message"] = "Invalid verification token.";
            TempData["MessageType"] = "error";
            return RedirectToAction("SignUp", "Account");
        }

        // Check expiry
        if (user.VerificationTokenExpiresAt < DateTime.UtcNow)
        {
            TempData["Message"] = "Your verification link has expired. Please request a new one.";
            TempData["MessageType"] = "error";
            return RedirectToAction("SignupConfirmation", "Account", new { email = user.Email, planId = user.PlanId });
        }

        user.IsEmailVerified = true;
        user.VerificationToken = null;
        user.VerificationTokenExpiresAt = null;
        await _context.SaveChangesAsync();

        TempData["Message"] = "Your email has been successfully verified! Please confirm your plan.";
        TempData["MessageType"] = "success";
        return RedirectToAction("ConfirmPlan", "Account", new { planId = planId, email = email });
    }
    // Called from email verification link — email comes from URL
    [HttpGet]
    public IActionResult ConfirmPlan(int planId, string email)
    {
        SetPaddleViewBag();

        var selectedPlan = _context.Plans.FirstOrDefault(p => p.Id == planId);

        if (selectedPlan == null)
        {
            TempData["Message"] = "Your email has been successfully verified! Please choose your plan.";
            TempData["MessageType"] = "success";
            ViewBag.Email = email;
            return View();
        }

        ViewBag.PlanId = selectedPlan.Id;
        ViewBag.PlanName = selectedPlan.PlanName;
        ViewBag.PlanPrice = selectedPlan.Price;
        ViewBag.PriceId = selectedPlan.PaddlePriceId;
        ViewBag.Email = email;

        return View();
    }

    // Called from Login when user has no active subscription — email comes from claims
    [HttpGet]
    public async Task<IActionResult> ChoosePlan()
    {
        SetPaddleViewBag();

        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(email))
            return RedirectToAction("Login");

        var plans = await _context.Plans.OrderBy(p => p.Id).ToListAsync();

        ViewBag.Email = email;
        ViewBag.Plans = plans;

        return View("ConfirmPlan");  // reuse the same view
    }

    // Reads Paddle client-side config from appsettings (environment-aware):
    //   Development → appsettings.Development.json → sandbox token + sandbox price IDs
    //   Production  → appsettings.json             → live token + live price IDs
    private void SetPaddleViewBag()
    {
        ViewBag.PaddleClientToken   = _configuration["Paddle:ClientToken"];
        ViewBag.PaddleIsSandbox     = bool.Parse(_configuration["Paddle:IsSandbox"] ?? "false");
        ViewBag.PaddleStarterPriceId = _configuration["Paddle:StarterPriceId"];
        ViewBag.PaddleGrowthPriceId  = _configuration["Paddle:GrowthPriceId"];
        ViewBag.PaddleProPriceId     = _configuration["Paddle:ProPriceId"];
    }

    [HttpPost]
    public async Task<IActionResult> ResendVerification(string email, int planId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            TempData["Message"] = "User not found.";
            TempData["MessageType"] = "error";
            return RedirectToAction("Index", "Home");
        }

        // Rate limiting — 30 seconds between resend requests
        var timeDifference = DateTime.UtcNow - user.ResendRequestTime.GetValueOrDefault();
        if (timeDifference.TotalSeconds < 30)
        {
            TempData["Message"] = "Please wait 30 seconds before requesting another verification email.";
            TempData["MessageType"] = "warning";
            return RedirectToAction("SignupConfirmation", "Account", new { email = email, planId = planId });
        }

        // Generate fresh token with new expiry
        var newToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        user.VerificationToken = newToken;
        user.VerificationTokenExpiresAt = DateTime.UtcNow.AddHours(24);
        user.ResendRequestTime = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var verificationUrl = Url.Action("VerifyEmail", "Account",
            new { token = newToken, planId = planId, email = email },
            Request.Scheme);

        await _emailService.SendVerificationEmailAsync(user.Email, verificationUrl);

        TempData["Message"] = "A new verification email has been sent.";
        TempData["MessageType"] = "success";
        return RedirectToAction("SignupConfirmation", "Account", new { email = email, planId = planId });
    }
    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity.IsAuthenticated)
            return RedirectToAction("Dashboard", "Leads");
        ViewData["Title"] = "Login";
        ViewData["Description"] = "Sign in to your Scoutix account to access your leads and dashboard.";
        return View(new LoginViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Message"] = "Please fill out all required fields.";
            TempData["MessageType"] = "warning";
            return View(model);
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
        if (user == null)
        {
            TempData["Message"] = "Invalid login attempt. Email not found.";
            TempData["MessageType"] = "error";
            return View(model);
        }

        // Check lockout before anything else
        if (user.IsLockedOut)
        {
            TempData["Message"] = "Your account has been locked due to too many failed login attempts. Please reset your password.";
            TempData["MessageType"] = "error";
            return View(model);
        }
        if (user.IsActive == false)
        {
            TempData["Message"] = "This account has been deactivated. Please contact support to reactivate.";
            TempData["MessageType"] = "error";
            return View(model);
        }
        if (!user.IsEmailVerified.GetValueOrDefault())
        {
            TempData["Message"] = "Your account is not yet activated. Please verify your email address.";
            TempData["MessageType"] = "warning";
            return View(model);
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, model.Password);
        if (verificationResult != PasswordVerificationResult.Success)
        {
            user.FailedLoginCount++;

            if (user.FailedLoginCount >= 5)
            {
                user.IsLockedOut = true;
                user.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                TempData["Message"] = "Your account has been locked due to too many failed login attempts. Please reset your password.";
                TempData["MessageType"] = "error";
                return View(model);
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var attemptsLeft = 5 - user.FailedLoginCount;
            TempData["Message"] = $"Invalid login attempt. Incorrect password. {attemptsLeft} attempt{(attemptsLeft == 1 ? "" : "s")} remaining before lockout.";
            TempData["MessageType"] = "error";
            return View(model);
        }

        // Successful login — reset failed count
        user.FailedLoginCount = 0;
        user.IsLockedOut = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var claims = new List<Claim>
{
    new Claim(ClaimTypes.Name, user.UserName),
    new Claim(ClaimTypes.Email, user.Email),
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
};

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddMinutes(30)
        };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        // Check if user has an active subscription
        var hasActiveSubscription = await _context.Subscriptions
            .AnyAsync(s => s.UserId == user.Id && s.IsSubscriptionActive == true);

        if (!hasActiveSubscription)
        {
            TempData["Message"] = "Please complete your subscription to get started.";
            TempData["MessageType"] = "info";
            return RedirectToAction("ChoosePlan", "Account");
        }

        TempData["Message"] = "Welcome back! You have successfully logged in.";
        TempData["MessageType"] = "success";
        return RedirectToAction("ManageLeads", "Leads");
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Message"] = "Logout Successful";
        TempData["MessageType"] = "success";
        return RedirectToAction("Login", "Account");
    }
    public IActionResult ForgotPassword()
    {
        ViewData["Title"] = "Forgot Password";
        ViewData["Description"] = "Reset your Scoutix account password.";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> ForgotPassword(string email)
    {
        if (string.IsNullOrEmpty(email))
            return View();

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

        // Always show success even if email not found — prevents user enumeration
        if (user == null)
        {
            TempData["ShowSuccess"] = true;
            return View();
        }

        // Generate token
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        user.PasswordResetToken = token;
        user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddHours(1);
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Build reset URL
        var resetUrl = Url.Action("ResetPassword", "Account",
            new { token = token, email = email },
            protocol: Request.Scheme);

        await _emailService.SendPasswordResetEmailAsync(email, resetUrl);

        _logger.LogInformation("Password reset email sent to {Email}", email);

        TempData["ShowSuccess"] = true;
        return View();
    }
    [HttpGet]
    public async Task<IActionResult> ResetPassword(string token, string email)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(email))
            return RedirectToAction("Login");

        var user = await _context.Users.FirstOrDefaultAsync(u =>
            u.Email == email &&
            u.PasswordResetToken == token &&
            u.PasswordResetTokenExpiresAt > DateTime.UtcNow);

        if (user == null)
        {
            TempData["Message"] = "This reset link is invalid or has expired.";
            TempData["MessageType"] = "error";
            return RedirectToAction("ForgotPassword");
        }

        ViewBag.Token = token;
        ViewBag.Email = email;
        return View();
    }
    [HttpPost]
    public async Task<IActionResult> ResetPassword(string token, string email, string newPassword, string confirmPassword)
    {
        if (newPassword != confirmPassword)
        {
            TempData["Message"] = "Passwords do not match.";
            TempData["MessageType"] = "error";
            ViewBag.Token = token;
            ViewBag.Email = email;
            return View();
        }

        var user = await _context.Users.FirstOrDefaultAsync(u =>
            u.Email == email &&
            u.PasswordResetToken == token &&
            u.PasswordResetTokenExpiresAt > DateTime.UtcNow);

        if (user == null)
        {
            TempData["Message"] = "This reset link is invalid or has expired.";
            TempData["MessageType"] = "error";
            return RedirectToAction("ForgotPassword");
        }

        // Hash and save new password
        user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiresAt = null;
        user.FailedLoginCount = 0;
        user.IsLockedOut = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Password reset successful for {Email}", email);

        TempData["Message"] = "Password reset successful. Please log in with your new password.";
        TempData["MessageType"] = "success";
        return RedirectToAction("Login");
    }
    public async Task<IActionResult> CheckoutSuccess([FromQuery(Name = "_ptxn")] string transactionId)
    {
        if (!string.IsNullOrEmpty(transactionId))
        {
            try
            {
                var apiKey = _configuration["Paddle:ApiKey"];
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                var paddleBaseUrl = _configuration["Paddle:BaseUrl"]?.TrimEnd('/');
                var response = await httpClient.GetAsync($"{paddleBaseUrl}/transactions/{transactionId}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Paddle API returned non-success on CheckoutSuccess. Status: {Status}, Body: {Body}",
                        response.StatusCode, errorBody);
                    return View();
                }

                var json = await response.Content.ReadAsStringAsync();
                dynamic transaction = JsonConvert.DeserializeObject<dynamic>(json);

                string priceId = transaction.data.items[0].price.id?.ToString();
                string email = transaction.data.custom_data?.email?.ToString();

                if (string.IsNullOrEmpty(email))
                {
                    _logger.LogWarning("CheckoutSuccess: email missing from transaction custom_data. TxnId: {TxnId}", transactionId);
                    return View();
                }

                var plan = await _context.Plans.FirstOrDefaultAsync(p => p.PaddlePriceId == priceId);
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

                if (user == null)
                {
                    _logger.LogWarning("CheckoutSuccess: no user found for email {Email}. TxnId: {TxnId}", email, transactionId);
                    return View();
                }

                if (plan == null)
                {
                    _logger.LogWarning("CheckoutSuccess: no plan found for priceId {PriceId}. TxnId: {TxnId}", priceId, transactionId);
                    return View();
                }

                var existingSubscription = await _context.Subscriptions
                    .FirstOrDefaultAsync(s => s.UserId == user.Id);

                if (existingSubscription != null && existingSubscription.IsSubscriptionActive == true)
                {
                    // Already active — duplicate call, skip
                    _logger.LogInformation("CheckoutSuccess: user {Email} already has active subscription, skipping. TxnId: {TxnId}", email, transactionId);
                }
                else if (existingSubscription != null && existingSubscription.IsSubscriptionActive != true)
                {
                    // Resubscription — reactivate existing row
                    existingSubscription.PlanId = plan.Id;
                    existingSubscription.IsSubscriptionActive = true;
                    existingSubscription.SubscriptionStatus = "active";
                    existingSubscription.StartDate = DateTime.UtcNow;
                    existingSubscription.EndDate = DateTime.UtcNow.AddMonths(1);
                    existingSubscription.CanceledAt = null;
                    existingSubscription.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    user.PlanId = plan.Id;
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("CheckoutSuccess: resubscription processed for {Email}. TxnId: {TxnId}", email, transactionId);
                }
                else
                {
                    // Brand new subscription
                    var subscription = new Subscription
                    {
                        UserId = user.Id,
                        PlanId = plan.Id,
                        IsSubscriptionActive = true,
                        SubscriptionStatus = "active",
                        StartDate = DateTime.UtcNow,
                        EndDate = DateTime.UtcNow.AddMonths(1),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    _context.Subscriptions.Add(subscription);
                    await _context.SaveChangesAsync();

                    user.SubscriptionId = subscription.Id;
                    user.PlanId = plan.Id;
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("CheckoutSuccess: new subscription created for {Email}, plan {PlanId}. TxnId: {TxnId}",
                        email, plan.Id, transactionId);
                }

                // Sign in only if not already authenticated
                if (!User.Identity.IsAuthenticated)
                {
                    var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.UserName),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
                };

                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var authProperties = new AuthenticationProperties
                    {
                        IsPersistent = false,
                        ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
                    };

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(claimsIdentity),
                        authProperties);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckoutSuccess: unexpected error processing transaction {TxnId}", transactionId);
            }
        }

        return View();
    }
    public IActionResult PrivacyPolicy()
    {
        ViewData["Title"] = "Privacy Policy";
        ViewData["Description"] = "Read the Scoutix privacy policy to understand how we handle your data.";
        return View();
    }

    public IActionResult TermsOfService()
    {
        ViewData["Title"] = "Terms of Service";
        ViewData["Description"] = "Read the Scoutix terms of service before using our platform.";
        return View();
    }
    [HttpGet]
    public IActionResult Checkout(int planId)
    {
        // Retrieve the selected plan from the database
        var selectedPlan = _context.Plans.FirstOrDefault(p => p.Id == planId);

        if (selectedPlan == null)
        {
            TempData["Message"] = "Selected plan not found.";
            TempData["MessageType"] = "error";
            return RedirectToAction("ConfirmPlan", "Account");
        }

        // Pass plan details and priceId to the view
        ViewBag.PriceId = selectedPlan.PaddlePriceId; // The Paddle priceId
        ViewBag.PlanName = selectedPlan.PlanName;
        ViewBag.PlanPrice = selectedPlan.Price;

        return View(); // Render the checkout page
    }
    public IActionResult SignupConfirmation(string email, int planId)
    {
        TempData["Email"] = email;
        TempData["PlanId"] = planId;
        return View();
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmNewPassword)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out int userId))
        {
            TempData["Message"] = "Session error. Please log in again.";
            TempData["MessageType"] = "error";
            return RedirectToAction("Settings", "Leads");
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return RedirectToAction("Logout");

        // Verify current password
        var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            TempData["Message"] = "Current password is incorrect.";
            TempData["MessageType"] = "error";
            return RedirectToAction("Settings", "Leads");
        }

        // Validate new password
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            TempData["Message"] = "New password must be at least 8 characters.";
            TempData["MessageType"] = "error";
            return RedirectToAction("Settings", "Leads");
        }

        // Confirm match
        if (newPassword != confirmNewPassword)
        {
            TempData["Message"] = "New passwords do not match.";
            TempData["MessageType"] = "error";
            return RedirectToAction("Settings", "Leads");
        }

        // Check new password is not same as current
        var sameAsOld = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, newPassword);
        if (sameAsOld != PasswordVerificationResult.Failed)
        {
            TempData["Message"] = "New password cannot be the same as your current password.";
            TempData["MessageType"] = "error";
            return RedirectToAction("Settings", "Leads");
        }

        // Update password
        user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["Message"] = "Password updated successfully.";
        TempData["MessageType"] = "success";
        return RedirectToAction("Settings", "Leads");
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAccount()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out int userId))
        {
            TempData["ErrorMessage"] = "Session error. Please log in again.";
            return RedirectToAction("Settings", "Leads");
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return RedirectToAction("Logout");

        // Step 1 — Cancel Paddle subscription if active
        var subscription = await _context.Subscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.IsSubscriptionActive == true);

        if (subscription != null && !string.IsNullOrEmpty(subscription.PaddleSubscriptionId))
        {
            try
            {
                var apiKey = _configuration["Paddle:ApiKey"];
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var paddleBaseUrl = _configuration["Paddle:BaseUrl"]?.TrimEnd('/');
                var cancelUrl = $"{paddleBaseUrl}/subscriptions/{subscription.PaddleSubscriptionId}/cancel";
                var payload = new { effective_from = "immediately" };

                var request = new HttpRequestMessage(HttpMethod.Post, cancelUrl)
                {
                    Content = new StringContent(
                        System.Text.Json.JsonSerializer.Serialize(payload),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };

                var response = await httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Paddle cancel failed during account deletion. Status: {Status}, Body: {Body}",
                        response.StatusCode, errorBody);
                    // Don't block deletion if Paddle fails — log and continue
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Network error cancelling Paddle subscription for user {UserId}", userId);
                // Don't block deletion if Paddle fails — log and continue
            }
        }

        // Step 2 — Cancel any active Hangfire job
        if (!string.IsNullOrEmpty(user.ActiveJobId))
        {
            try
            {
                BackgroundJob.Delete(user.ActiveJobId);
            }
            catch
            {
                // silently ignore if job already finished
            }
            user.ActiveJobId = null;
        }

        // Step 3 — Soft delete user
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        user.ActiveJobId = null;
        await _context.SaveChangesAsync();

        // Step 4 — Sign out
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // Step 5 — Redirect to login with message
        TempData["Message"] = "Your account has been deactivated. We're sorry to see you go.";
        TempData["MessageType"] = "info";
        return RedirectToAction("Login", "Account");
    }
}

