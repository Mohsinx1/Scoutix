namespace Scoutix.Services.EmailService
{
    public interface IEmailService
    {
        Task SendVerificationEmailAsync(string toEmail, string verificationUrl);
        Task SendReceiptEmailAsync(string toEmail, string transactionId, string amount, string planName, string paymentDate, string billingStart, string billingEnd, string nextBillingDate);
        Task SendPasswordResetEmailAsync(string toEmail, string resetUrl);
        Task SendPaymentFailedEmailAsync(string toEmail, string planName);
        Task SendSubscriptionCanceledEmailAsync(string toEmail, string planName, string accessEndsAt);
        Task SendPastDueEmailAsync(string toEmail, string planName);
        Task SendContactFormEmailAsync(string fromName, string fromEmail, string subject, string message);
    }
}
