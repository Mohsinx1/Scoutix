using System.Text;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Scoutix.Services.EmailService
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        private string ApiUrl => _configuration["MailgunSettings:ApiUrl"];
        private string ApiKey => _configuration["MailgunSettings:ApiKey"];
        private string FromAddress => _configuration["MailgunSettings:FromAddress"];

        public EmailService(
            IConfiguration configuration,
            ILogger<EmailService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        // ─────────────────────────────────────────────
        // 1. Verification Email
        // ─────────────────────────────────────────────
        public async Task SendVerificationEmailAsync(string toEmail, string verificationUrl)
        {
            var subject = "Verify your Scoutix account";
            var html = $@"
<!DOCTYPE html>
<html lang='en'>
<head><meta charset='UTF-8'/><meta name='viewport' content='width=device-width, initial-scale=1.0'/></head>
<body style='margin:0;padding:0;background-color:#F0FDF4;font-family:Arial,Helvetica,sans-serif;'>
<table width='100%' cellpadding='0' cellspacing='0' style='background-color:#F0FDF4;padding:40px 16px;'>
<tr><td align='center'>
<table width='100%' style='max-width:560px;' cellpadding='0' cellspacing='0'>

    {BrandHeader()}

    <tr><td style='{CardStyle()}'>
        {TopAccentBar()}
        <table width='100%' cellpadding='0' cellspacing='0'><tr><td style='padding:40px 40px 32px;'>
            {IconBlock("&#9993;")}
            {Heading("Verify your email address")}
            {SubText($"Thanks for signing up for <strong style='color:#059669;'>Scoutix</strong>! Click the button below to verify your email and activate your account.")}
            {CtaButton(verificationUrl, "Verify My Email &rarr;")}
            {InfoBox("&#9200; This link expires in <strong>24 hours</strong>. If you did not create an account, you can safely ignore this email.")}
            {Divider()}
            {FallbackUrl(verificationUrl)}
        </td></tr></table>
        {WhatNextStrip(new[]
        {
            ("&#10003;", "Verify email", "Click the button above"),
            ("&#9733;", "Choose a plan", "Pick what fits your needs"),
            ("&#128640;", "Generate leads", "Start finding leads instantly")
        })}
    </td></tr>

    {Footer()}
</table>
</td></tr></table>
</body></html>";

            await SendAsync(toEmail, subject, html);
        }

        // ─────────────────────────────────────────────
        // 2. Receipt Email
        // ─────────────────────────────────────────────
        public async Task SendReceiptEmailAsync(
            string toEmail,
            string transactionId,
            string amount,
            string planName,
            string paymentDate,
            string billingStart,
            string billingEnd,
            string nextBillingDate)
        {
            var subject = $"Payment Receipt — {planName} — Scoutix";
            var html = $@"
<!DOCTYPE html>
<html lang='en'>
<head><meta charset='UTF-8'/><meta name='viewport' content='width=device-width, initial-scale=1.0'/></head>
<body style='margin:0;padding:0;background-color:#F0FDF4;font-family:Arial,Helvetica,sans-serif;'>
<table width='100%' cellpadding='0' cellspacing='0' style='background-color:#F0FDF4;padding:40px 16px;'>
<tr><td align='center'>
<table width='100%' style='max-width:560px;' cellpadding='0' cellspacing='0'>

    {BrandHeader()}

    <tr><td style='{CardStyle()}'>
        {TopAccentBar()}
        <table width='100%' cellpadding='0' cellspacing='0'><tr><td style='padding:40px 40px 32px;'>
            {IconBlock("&#10003;")}
            {Heading("Payment Confirmed!")}
            {SubText($"Thank you for your payment. Your <strong style='color:#059669;'>{planName}</strong> subscription is active and ready to use.")}
            {AmountBox(amount, planName)}
            {ReceiptTable(transactionId, paymentDate, billingStart, billingEnd, nextBillingDate)}
            {InfoBox("&#9432; You can update your payment method, cancel, or download invoices anytime from your <strong style='color:#059669;'>Billing Portal</strong> in account settings.")}
            {Divider()}
            {SupportNote()}
        </td></tr></table>
        {WhatNextStrip(new[]
        {
            ("&#128270;", "Lead Generation", "Find business leads fast"),
            ("&#128101;", "Built-in CRM", "Manage your contacts"),
            ("&#128196;", "CSV Export", "Download your data")
        })}
    </td></tr>

    {Footer()}
</table>
</td></tr></table>
</body></html>";

            await SendAsync(toEmail, subject, html);
        }

        // ─────────────────────────────────────────────
        // 3. Password Reset Email
        // ─────────────────────────────────────────────
        public async Task SendPasswordResetEmailAsync(string toEmail, string resetUrl)
        {
            var subject = "Reset your Scoutix password";
            var html = $@"
<!DOCTYPE html>
<html lang='en'>
<head><meta charset='UTF-8'/><meta name='viewport' content='width=device-width, initial-scale=1.0'/></head>
<body style='margin:0;padding:0;background-color:#F0FDF4;font-family:Arial,Helvetica,sans-serif;'>
<table width='100%' cellpadding='0' cellspacing='0' style='background-color:#F0FDF4;padding:40px 16px;'>
<tr><td align='center'>
<table width='100%' style='max-width:560px;' cellpadding='0' cellspacing='0'>

    {BrandHeader()}

    <tr><td style='{CardStyle()}'>
        {TopAccentBar()}
        <table width='100%' cellpadding='0' cellspacing='0'><tr><td style='padding:40px 40px 32px;'>
            {IconBlock("&#128274;")}
            {Heading("Reset your password")}
            {SubText("We received a request to reset your Scoutix password. Click the button below to set a new password.")}
            {CtaButton(resetUrl, "Reset My Password &rarr;")}
            {InfoBox("&#9200; This link expires in <strong>1 hour</strong>. If you did not request a password reset, you can safely ignore this email.")}
            {Divider()}
            {FallbackUrl(resetUrl)}
        </td></tr></table>
    </td></tr>

    {Footer()}
</table>
</td></tr></table>
</body></html>";

            await SendAsync(toEmail, subject, html);
        }

        // ─────────────────────────────────────────────
        // 4. Payment Failed Email
        // ─────────────────────────────────────────────
        public async Task SendPaymentFailedEmailAsync(string toEmail, string planName)
        {
            var subject = "Action Required — Payment Failed — Scoutix";
            var html = $@"
<!DOCTYPE html>
<html lang='en'>
<head><meta charset='UTF-8'/><meta name='viewport' content='width=device-width, initial-scale=1.0'/></head>
<body style='margin:0;padding:0;background-color:#F0FDF4;font-family:Arial,Helvetica,sans-serif;'>
<table width='100%' cellpadding='0' cellspacing='0' style='background-color:#F0FDF4;padding:40px 16px;'>
<tr><td align='center'>
<table width='100%' style='max-width:560px;' cellpadding='0' cellspacing='0'>

    {BrandHeader()}

    <tr><td style='{CardStyle()}'>
        {TopAccentBar()}
        <table width='100%' cellpadding='0' cellspacing='0'><tr><td style='padding:40px 40px 32px;'>
            {IconBlock("&#9888;", "#DC2626")}
            {Heading("Payment Failed")}
            {SubText($"We were unable to process your payment for the <strong style='color:#059669;'>{planName}</strong> plan. Please update your payment method to keep your access.")}
            {WarningBox("&#9888; Your access may be suspended if payment is not resolved. Please update your payment method as soon as possible.")}
            {Divider()}
            {SupportNote()}
        </td></tr></table>
    </td></tr>

    {Footer()}
</table>
</td></tr></table>
</body></html>";

            await SendAsync(toEmail, subject, html);
        }

        // ─────────────────────────────────────────────
        // 5. Subscription Canceled Email
        // ─────────────────────────────────────────────
        public async Task SendSubscriptionCanceledEmailAsync(string toEmail, string planName, string accessEndsAt)
        {
            var subject = "Your Scoutix subscription has been canceled";
            var html = $@"
<!DOCTYPE html>
<html lang='en'>
<head><meta charset='UTF-8'/><meta name='viewport' content='width=device-width, initial-scale=1.0'/></head>
<body style='margin:0;padding:0;background-color:#F0FDF4;font-family:Arial,Helvetica,sans-serif;'>
<table width='100%' cellpadding='0' cellspacing='0' style='background-color:#F0FDF4;padding:40px 16px;'>
<tr><td align='center'>
<table width='100%' style='max-width:560px;' cellpadding='0' cellspacing='0'>

    {BrandHeader()}

    <tr><td style='{CardStyle()}'>
        {TopAccentBar()}
        <table width='100%' cellpadding='0' cellspacing='0'><tr><td style='padding:40px 40px 32px;'>
            {IconBlock("&#128197;")}
            {Heading("Subscription Canceled")}
            {SubText($"Your <strong style='color:#059669;'>{planName}</strong> subscription has been canceled. You will continue to have full access until <strong>{accessEndsAt}</strong>.")}
            {InfoBox($"&#9432; Your access remains active until <strong>{accessEndsAt}</strong>. After that date your account will be downgraded. You can resubscribe anytime.")}
            {Divider()}
            {SupportNote()}
        </td></tr></table>
    </td></tr>

    {Footer()}
</table>
</td></tr></table>
</body></html>";

            await SendAsync(toEmail, subject, html);
        }

        // ─────────────────────────────────────────────
        // 6. Past Due Email
        // ─────────────────────────────────────────────
        public async Task SendPastDueEmailAsync(string toEmail, string planName)
        {
            var subject = "Action Required — Payment Past Due — Scoutix";
            var html = $@"
<!DOCTYPE html>
<html lang='en'>
<head><meta charset='UTF-8'/><meta name='viewport' content='width=device-width, initial-scale=1.0'/></head>
<body style='margin:0;padding:0;background-color:#F0FDF4;font-family:Arial,Helvetica,sans-serif;'>
<table width='100%' cellpadding='0' cellspacing='0' style='background-color:#F0FDF4;padding:40px 16px;'>
<tr><td align='center'>
<table width='100%' style='max-width:560px;' cellpadding='0' cellspacing='0'>

    {BrandHeader()}

    <tr><td style='{CardStyle()}'>
        {TopAccentBar()}
        <table width='100%' cellpadding='0' cellspacing='0'><tr><td style='padding:40px 40px 32px;'>
            {IconBlock("&#9888;", "#B7791F")}
            {Heading("Payment Past Due")}
            {SubText($"Your payment for the <strong style='color:#059669;'>{planName}</strong> plan is past due. Please update your payment method to avoid losing access.")}
            {WarningBox("&#9888; Paddle will retry your payment automatically. To avoid interruption please update your payment method in the billing portal.")}
            {Divider()}
            {SupportNote()}
        </td></tr></table>
    </td></tr>

    {Footer()}
</table>
</td></tr></table>
</body></html>";

            await SendAsync(toEmail, subject, html);
        }

        // ─────────────────────────────────────────────
        // 7. Contact Form Email
        // ─────────────────────────────────────────────
        public async Task SendContactFormEmailAsync(string fromName, string fromEmail, string subject, string message)
        {
            var emailSubject = $"Contact Form: {subject}";
            var html = $@"
<!DOCTYPE html>
<html lang='en'>
<head><meta charset='UTF-8'/><meta name='viewport' content='width=device-width, initial-scale=1.0'/></head>
<body style='margin:0;padding:0;background-color:#F0FDF4;font-family:Arial,Helvetica,sans-serif;'>
<table width='100%' cellpadding='0' cellspacing='0' style='background-color:#F0FDF4;padding:40px 16px;'>
<tr><td align='center'>
<table width='100%' style='max-width:560px;' cellpadding='0' cellspacing='0'>

    {BrandHeader()}

    <tr><td style='{CardStyle()}'>
        {TopAccentBar()}
        <table width='100%' cellpadding='0' cellspacing='0'><tr><td style='padding:40px 40px 32px;'>
            {IconBlock("&#9993;")}
            {Heading("New Contact Form Submission")}
            {SubText($"You have received a new message from <strong style='color:#059669;'>{fromName}</strong> via the Scoutix contact form.")}
            <table width='100%' cellpadding='0' cellspacing='0' style='margin-bottom:28px;border:1px solid #E5E7EB;border-radius:12px;overflow:hidden;'>
                <tr><td style='background:#F9FAFB;padding:12px 18px;border-bottom:1px solid #E5E7EB;'>
                    <table width='100%' cellpadding='0' cellspacing='0'><tr>
                        <td style='font-size:13px;color:#6B7280;'>Name</td>
                        <td align='right' style='font-size:13px;color:#1A1A1A;font-weight:600;'>{fromName}</td>
                    </tr></table>
                </td></tr>
                <tr><td style='background:#ffffff;padding:12px 18px;border-bottom:1px solid #E5E7EB;'>
                    <table width='100%' cellpadding='0' cellspacing='0'><tr>
                        <td style='font-size:13px;color:#6B7280;'>Email</td>
                        <td align='right' style='font-size:13px;color:#059669;font-weight:600;'>{fromEmail}</td>
                    </tr></table>
                </td></tr>
                <tr><td style='background:#F9FAFB;padding:12px 18px;border-bottom:1px solid #E5E7EB;'>
                    <table width='100%' cellpadding='0' cellspacing='0'><tr>
                        <td style='font-size:13px;color:#6B7280;'>Subject</td>
                        <td align='right' style='font-size:13px;color:#1A1A1A;font-weight:600;'>{subject}</td>
                    </tr></table>
                </td></tr>
                <tr><td style='background:#ffffff;padding:16px 18px;'>
                    <p style='margin:0 0 6px;font-size:13px;color:#6B7280;'>Message</p>
                    <p style='margin:0;font-size:14px;color:#1A1A1A;line-height:1.7;white-space:pre-wrap;'>{message}</p>
                </td></tr>
            </table>
            {InfoBox($"&#9993; Reply directly to <strong style='color:#059669;'>{fromEmail}</strong> to respond to this inquiry.")}
        </td></tr></table>
    </td></tr>

    <tr><td style='padding:20px 16px 0;text-align:center;'>
        <p style='margin:0;font-size:12px;color:#9CA3AF;'>&copy; {DateTime.UtcNow.Year} Scoutix. Internal notification.</p>
    </td></tr>
</table>
</td></tr></table>
</body></html>";

            await SendAsync("support@scoutix.io", emailSubject, html);
        }

        // ═════════════════════════════════════════════
        // PRIVATE — Core Mailgun Sender
        // ═════════════════════════════════════════════
        private async Task SendAsync(string toEmail, string subject, string html)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "from", FromAddress },
                        { "to", toEmail },
                        { "subject", subject },
                        { "html", html }
                    })
                };
                request.Headers.Add("Authorization",
                    "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("api:" + ApiKey)));

                var response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                    _logger.LogInformation("Email sent to {Email} — Subject: {Subject}", toEmail, subject);
                else
                    _logger.LogError("Failed to send email to {Email}. Status: {Status}", toEmail, response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception sending email to {Email}", toEmail);
            }
        }

        // ═════════════════════════════════════════════
        // PRIVATE — Reusable HTML Building Blocks
        // ═════════════════════════════════════════════
        private string BrandHeader() => @"
<tr><td align='center' style='padding-bottom:24px;'>
    <table cellpadding='0' cellspacing='0'><tr>
        <td style='background:linear-gradient(135deg,#059669,#047857);border-radius:14px;padding:12px 24px;'>
            <span style='color:#FCD34D;font-size:22px;font-weight:800;letter-spacing:0.5px;'>&#9670; Scoutix</span>
        </td>
    </tr></table>
</td></tr>";

        private string CardStyle() => @"
background:#ffffff;
border-radius:24px;
box-shadow:0 8px 40px rgba(0,0,0,0.08);
border:1px solid rgba(5,150,105,0.12);
overflow:hidden;";

        private string TopAccentBar() => @"
<table width='100%' cellpadding='0' cellspacing='0'><tr>
    <td style='background:linear-gradient(90deg,#059669,#047857);height:5px;font-size:0;line-height:0;'>&nbsp;</td>
</tr></table>";

        private string IconBlock(string icon, string bgColor = "#059669") => $@"
<table cellpadding='0' cellspacing='0' style='margin:0 auto 24px;'><tr>
    <td align='center' style='width:72px;height:72px;background:linear-gradient(135deg,{bgColor},{bgColor});border-radius:16px;font-size:32px;line-height:72px;text-align:center;color:white;'>
        {icon}
    </td>
</tr></table>";

        private string Heading(string text) => $@"
<h1 style='margin:0 0 10px;font-size:26px;font-weight:800;color:#1A1A1A;text-align:center;line-height:1.3;'>
    {text}
</h1>";

        private string SubText(string text) => $@"
<p style='margin:0 0 28px;font-size:15px;color:#6B7280;text-align:center;line-height:1.7;'>
    {text}
</p>";

        private string CtaButton(string url, string label) => $@"
<table cellpadding='0' cellspacing='0' style='margin:0 auto 28px;'><tr>
    <td align='center' style='background:#059669;border-radius:12px;'>
        <a href='{url}' style='display:inline-block;padding:16px 40px;font-size:16px;font-weight:700;color:#ffffff;text-decoration:none;border-radius:12px;letter-spacing:0.3px;'>
            {label}
        </a>
    </td>
</tr></table>";

        private string InfoBox(string text) => $@"
<table width='100%' cellpadding='0' cellspacing='0' style='margin-bottom:28px;'><tr>
    <td style='background:#F0FDF4;border:1px solid rgba(5,150,105,0.2);border-radius:10px;padding:14px 18px;font-size:13px;color:#374151;line-height:1.6;text-align:center;'>
        {text}
    </td>
</tr></table>";

        private string WarningBox(string text) => $@"
<table width='100%' cellpadding='0' cellspacing='0' style='margin-bottom:28px;'><tr>
    <td style='background:#FEF2F2;border:1px solid rgba(220,38,38,0.2);border-radius:10px;padding:14px 18px;font-size:13px;color:#374151;line-height:1.6;text-align:center;'>
        {text}
    </td>
</tr></table>";

        private string Divider() => @"
<table width='100%' cellpadding='0' cellspacing='0' style='margin-bottom:20px;'><tr>
    <td style='border-top:1px solid #E5E7EB;font-size:0;line-height:0;'>&nbsp;</td>
</tr></table>";

        private string FallbackUrl(string url) => $@"
<p style='margin:0;font-size:12px;color:#9CA3AF;text-align:center;line-height:1.7;'>
    Button not working? Copy and paste this link into your browser:<br/>
    <a href='{url}' style='color:#059669;word-break:break-all;font-size:11px;'>{url}</a>
</p>";

        private string SupportNote() => $@"
<p style='margin:0;font-size:12px;color:#9CA3AF;text-align:center;line-height:1.7;'>
    Questions? Contact us at <a href='mailto:support@scoutix.io' style='color:#059669;text-decoration:none;'>support@scoutix.io</a>
</p>";

        private string AmountBox(string amount, string planName) => $@"
<table width='100%' cellpadding='0' cellspacing='0' style='margin-bottom:28px;'><tr>
    <td align='center' style='background:linear-gradient(135deg,#064E3B,#065F46);border-radius:16px;padding:24px;'>
        <p style='margin:0 0 4px;font-size:13px;color:rgba(255,255,255,0.6);text-transform:uppercase;letter-spacing:0.08em;'>Amount Charged</p>
        <p style='margin:0;font-size:36px;font-weight:800;color:#FCD34D;line-height:1.2;'>{amount}</p>
        <p style='margin:4px 0 0;font-size:13px;color:rgba(255,255,255,0.5);'>{planName}</p>
    </td>
</tr></table>";

        private string ReceiptTable(string txnId, string paymentDate, string start, string end, string nextBilling) => $@"
<table width='100%' cellpadding='0' cellspacing='0' style='margin-bottom:28px;border:1px solid #E5E7EB;border-radius:12px;overflow:hidden;'>
    <tr><td style='background:#F9FAFB;padding:12px 18px;border-bottom:1px solid #E5E7EB;'>
        <table width='100%' cellpadding='0' cellspacing='0'><tr>
            <td style='font-size:13px;color:#6B7280;'>Transaction ID</td>
            <td align='right' style='font-size:13px;color:#1A1A1A;font-weight:600;word-break:break-all;'>{txnId}</td>
        </tr></table>
    </td></tr>
    <tr><td style='background:#ffffff;padding:12px 18px;border-bottom:1px solid #E5E7EB;'>
        <table width='100%' cellpadding='0' cellspacing='0'><tr>
            <td style='font-size:13px;color:#6B7280;'>Payment Date</td>
            <td align='right' style='font-size:13px;color:#1A1A1A;font-weight:600;'>{paymentDate}</td>
        </tr></table>
    </td></tr>
    <tr><td style='background:#F9FAFB;padding:12px 18px;border-bottom:1px solid #E5E7EB;'>
        <table width='100%' cellpadding='0' cellspacing='0'><tr>
            <td style='font-size:13px;color:#6B7280;'>Billing Period</td>
            <td align='right' style='font-size:13px;color:#1A1A1A;font-weight:600;'>{start} — {end}</td>
        </tr></table>
    </td></tr>
    <tr><td style='background:#ffffff;padding:12px 18px;'>
        <table width='100%' cellpadding='0' cellspacing='0'><tr>
            <td style='font-size:13px;color:#6B7280;'>Next Billing Date</td>
            <td align='right' style='font-size:13px;color:#059669;font-weight:700;'>{nextBilling}</td>
        </tr></table>
    </td></tr>
</table>";

        private string WhatNextStrip((string icon, string title, string desc)[] items)
        {
            var cells = string.Join("", items.Select(i => $@"
<td width='33%' style='text-align:center;padding:0 8px;'>
    <div style='font-size:22px;margin-bottom:6px;'>{i.icon}</div>
    <p style='margin:0;font-size:12px;color:#6B7280;line-height:1.5;'>
        <strong style='color:#1A1A1A;'>{i.title}</strong><br/>{i.desc}
    </p>
</td>"));

            return $@"
<table width='100%' cellpadding='0' cellspacing='0'><tr>
    <td style='background:#F9FAFB;border-top:1px solid #F3F4F6;padding:24px 40px;'>
        <p style='margin:0 0 14px;font-size:12px;font-weight:700;color:#9CA3AF;text-transform:uppercase;letter-spacing:0.08em;text-align:center;'>What happens next</p>
        <table width='100%' cellpadding='0' cellspacing='0'><tr>{cells}</tr></table>
    </td>
</tr></table>";
        }

        private string Footer() => $@"
<tr><td style='padding:28px 16px 0;text-align:center;'>
    <p style='margin:0 0 6px;font-size:12px;color:#9CA3AF;'>&copy; {DateTime.UtcNow.Year} Scoutix. All rights reserved.</p>
    <p style='margin:0;font-size:12px;color:#9CA3AF;'>
        <a href='#' style='color:#059669;text-decoration:none;'>Unsubscribe</a> &nbsp;|&nbsp;
        <a href='#' style='color:#059669;text-decoration:none;'>Privacy Policy</a>
    </p>
</td></tr>";
    }
}
