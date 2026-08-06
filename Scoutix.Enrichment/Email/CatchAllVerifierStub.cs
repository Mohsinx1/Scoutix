using Scoutix.Enrichment.Model;

namespace Scoutix.Enrichment.Email;

/// <summary>
/// The config-swappable slot for an external catch-all-capable verifier (a paid API). Until one is
/// wired in, catch-all domains stay unresolved — reported as <see cref="EmailVerificationStatus.CatchAll"/>
/// at reduced confidence. Pass an instance as the fallback to <see cref="SmtpEmailVerifier"/>.
/// </summary>
public sealed class CatchAllVerifierStub : IEmailVerifier
{
    public Task<EmailVerificationResult> VerifyAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(new EmailVerificationResult(
            email, EmailVerificationStatus.CatchAll, 0.5,
            EmailClassifier.IsRoleAddress(email), IsCatchAll: true,
            Detail: "catch-all unresolved (external verifier not configured)"));
}
