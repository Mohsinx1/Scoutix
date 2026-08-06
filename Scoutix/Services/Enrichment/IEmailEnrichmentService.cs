using Scoutix.Models.Enums;

namespace Scoutix.Services.Enrichment;

public interface IEmailEnrichmentService
{
    Task<EnrichmentResult> EnrichLeadAsync(int leadId, CancellationToken ct = default);
}

public class EnrichmentResult
{
    public bool Success { get; set; }
    public string? BestEmail { get; set; }
    public List<string> AllEmails { get; set; } = new();
    public EnrichmentStatus FinalStatus { get; set; }
    public string? FailureReason { get; set; }
}
