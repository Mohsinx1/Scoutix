using Scoutix.Enrichment.Model;

namespace Scoutix.Enrichment.Email;

/// <summary>
/// Pluggable email-deliverability check. The default is <see cref="SmtpEmailVerifier"/>; this is the
/// config-swappable slot where an external catch-all-capable API can be dropped in.
/// </summary>
public interface IEmailVerifier
{
    Task<EmailVerificationResult> VerifyAsync(string email, CancellationToken ct = default);
}

public sealed record EmailVerificationResult(
    string Email,
    EmailVerificationStatus Status,
    double Confidence,
    bool IsRole = false,
    bool IsCatchAll = false,
    string? Detail = null);
