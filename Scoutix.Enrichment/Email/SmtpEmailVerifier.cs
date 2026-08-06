using System.Net.Sockets;
using System.Text;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using Scoutix.Enrichment.Configuration;
using Scoutix.Enrichment.Model;

namespace Scoutix.Enrichment.Email;

/// <summary>
/// Built-in deliverability verifier: syntax → disposable → MX → SMTP RCPT probe, with catch-all
/// detection (a domain that accepts a random address can't confirm a specific mailbox).
///
/// Degrades gracefully: DNS/MX works almost anywhere, but SMTP needs outbound port 25, which many
/// networks (and most cloud hosts) block. When the probe can't run, the result is <c>Unknown</c>
/// rather than a false negative — and a real catch-all/external verifier can be supplied as a
/// fallback for production.
/// </summary>
public sealed class SmtpEmailVerifier : IEmailVerifier
{
    private readonly EmailVerifyOptions _opts;
    private readonly ILogger<SmtpEmailVerifier> _logger;
    private readonly IEmailVerifier? _catchAllFallback;
    private readonly LookupClient _dns = new();

    public SmtpEmailVerifier(EmailVerifyOptions opts, ILogger<SmtpEmailVerifier> logger, IEmailVerifier? catchAllFallback = null)
    {
        _opts = opts;
        _logger = logger;
        _catchAllFallback = catchAllFallback;
    }

    public async Task<EmailVerificationResult> VerifyAsync(string email, CancellationToken ct = default)
    {
        email = email.Trim();
        var isRole = EmailClassifier.IsRoleAddress(email);

        if (!EmailClassifier.IsValidSyntax(email))
            return new EmailVerificationResult(email, EmailVerificationStatus.Invalid, 0.0, isRole, Detail: "bad syntax");
        if (EmailClassifier.IsDisposableDomain(email))
            return new EmailVerificationResult(email, EmailVerificationStatus.Disposable, 0.1, isRole, Detail: "disposable domain");

        var domain = EmailClassifier.GetDomain(email)!;

        var mxHosts = await ResolveMailHostsAsync(domain, ct);
        if (mxHosts.Count == 0)
            return new EmailVerificationResult(email, EmailVerificationStatus.NoDomain, 0.0, isRole, Detail: "no MX/A record");

        if (!_opts.SmtpProbe)
            return Unknown(email, isRole, "smtp probe disabled (MX valid)");

        var probe = await ProbeAsync(mxHosts, email, domain, ct);
        if (probe is null)
            return Unknown(email, isRole, "smtp unreachable (port 25 blocked?)");

        var (codeEmail, isCatchAll) = probe.Value;

        if (codeEmail is >= 250 and < 260)
        {
            if (isCatchAll)
            {
                if (_catchAllFallback is not null)
                    return await _catchAllFallback.VerifyAsync(email, ct);
                return new EmailVerificationResult(email, EmailVerificationStatus.CatchAll, 0.5, isRole, IsCatchAll: true,
                    Detail: "catch-all domain (external verifier not configured)");
            }
            return new EmailVerificationResult(email, EmailVerificationStatus.Verified, isRole ? 0.8 : 0.9, isRole, Detail: "smtp 250");
        }

        if (codeEmail is >= 500 and < 600)
            return new EmailVerificationResult(email, EmailVerificationStatus.Invalid, 0.1, isRole, Detail: $"smtp {codeEmail}");

        return Unknown(email, isRole, $"smtp inconclusive ({codeEmail})");
    }

    private static EmailVerificationResult Unknown(string email, bool isRole, string detail) =>
        new(email, EmailVerificationStatus.Unknown, isRole ? 0.40 : 0.45, isRole, Detail: detail);

    private async Task<List<string>> ResolveMailHostsAsync(string domain, CancellationToken ct)
    {
        try
        {
            var mx = await _dns.QueryAsync(domain, QueryType.MX, cancellationToken: ct);
            var hosts = mx.Answers.MxRecords()
                .OrderBy(r => r.Preference)
                .Select(r => r.Exchange.Value.TrimEnd('.'))
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .ToList();
            if (hosts.Count > 0) return hosts;

            // No MX → some domains accept mail at their A record (implicit MX).
            var a = await _dns.QueryAsync(domain, QueryType.A, cancellationToken: ct);
            if (a.Answers.ARecords().Any()) return new List<string> { domain };
        }
        catch (Exception ex)
        {
            _logger.LogDebug("MX lookup failed for {Domain}: {Message}", domain, ex.Message);
        }
        return new List<string>();
    }

    /// <summary>Returns (rcptCode, isCatchAll) from the first mail host that answers, or null if none do.</summary>
    private async Task<(int Code, bool CatchAll)?> ProbeAsync(List<string> hosts, string email, string domain, CancellationToken ct)
    {
        foreach (var host in hosts.Take(2))
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(host, 25).WaitAsync(TimeSpan.FromMilliseconds(_opts.SmtpTimeoutMs), ct);

                using var stream = tcp.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };

                if (await ReadCodeAsync(reader, ct) != 220) continue;

                await writer.WriteLineAsync($"EHLO {_opts.EhloDomain}".AsMemory(), ct);
                if (await ReadCodeAsync(reader, ct) != 250) continue;

                await writer.WriteLineAsync($"MAIL FROM:<{_opts.MailFrom}>".AsMemory(), ct);
                if (await ReadCodeAsync(reader, ct) != 250) continue;

                await writer.WriteLineAsync($"RCPT TO:<{email}>".AsMemory(), ct);
                var codeEmail = await ReadCodeAsync(reader, ct);

                // Catch-all probe: does a random mailbox also get accepted?
                bool catchAll = false;
                if (codeEmail is >= 250 and < 260)
                {
                    await writer.WriteLineAsync($"RCPT TO:<nx{Guid.NewGuid():N}@{domain}>".AsMemory(), ct);
                    var codeRandom = await ReadCodeAsync(reader, ct);
                    catchAll = codeRandom is >= 250 and < 260;
                }

                try { await writer.WriteLineAsync("QUIT".AsMemory(), ct); } catch { /* ignore */ }
                return (codeEmail, catchAll);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("SMTP probe to {Host} failed: {Message}", host, ex.Message);
            }
        }
        return null;
    }

    private async Task<int> ReadCodeAsync(StreamReader reader, CancellationToken ct)
    {
        // SMTP replies can be multi-line ("250-..." continuations, final "250 ..."). Read until the
        // line where the 4th char is a space.
        string? line;
        while ((line = await reader.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromMilliseconds(_opts.SmtpTimeoutMs), ct)) is not null)
        {
            if (line.Length >= 4 && line[3] == ' ' && int.TryParse(line[..3], out var code))
                return code;
            if (line.Length >= 3 && line[3] != '-' && int.TryParse(line[..3], out var code2))
                return code2;
        }
        return 0;
    }
}
