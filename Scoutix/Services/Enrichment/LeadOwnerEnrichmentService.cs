using Microsoft.EntityFrameworkCore;
using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Model;
using Scoutix.Models;
using EnrichStatus = Scoutix.Models.Enums.EnrichmentStatus;

namespace Scoutix.Services.Enrichment;

/// <summary>Result of enriching one lead, mapped back to the app's status lifecycle.</summary>
public sealed class LeadEnrichmentOutcome
{
    public string? OwnerName { get; init; }
    public bool OwnerVerified { get; init; }
    public double OwnerConfidence { get; init; }
    public string? OwnerSources { get; init; }
    public string? Email { get; init; }
    public int EmailStatus { get; init; }
    public EnrichStatus FinalStatus { get; init; }
}

public interface ILeadOwnerEnrichmentService
{
    Task<LeadEnrichmentOutcome> EnrichLeadAsync(int leadId, CancellationToken ct = default);
}

/// <summary>
/// Adapter between a CRM <see cref="Lead"/> and the enrichment engine: maps the lead to an engine
/// <see cref="Listing"/>, runs the vertical-appropriate waterfall, and returns the resolved owner +
/// best email. Engine candidates/provenance persist to the engine tables; the job writes the resolved
/// values back onto the lead.
/// </summary>
public sealed class LeadOwnerEnrichmentService : ILeadOwnerEnrichmentService
{
    private readonly ApplicationDbContext _app;
    private readonly IListingStore _listings;
    private readonly ILeadEnricherFactory _enricherFactory;
    private readonly ILogger<LeadOwnerEnrichmentService> _logger;

    public LeadOwnerEnrichmentService(
        ApplicationDbContext app, IListingStore listings,
        ILeadEnricherFactory enricherFactory, ILogger<LeadOwnerEnrichmentService> logger)
    {
        _app = app;
        _listings = listings;
        _enricherFactory = enricherFactory;
        _logger = logger;
    }

    public async Task<LeadEnrichmentOutcome> EnrichLeadAsync(int leadId, CancellationToken ct = default)
    {
        var lead = await _app.Leads
            .Include(l => l.City)
            .Include(l => l.State)
            .Include(l => l.Niche)
            .FirstOrDefaultAsync(l => l.Id == leadId, ct);

        if (lead is null)
        {
            _logger.LogWarning("LeadOwnerEnrichment: lead {LeadId} not found.", leadId);
            return new LeadEnrichmentOutcome { FinalStatus = EnrichStatus.Failed };
        }

        var listing = new Listing
        {
            SourceKey = $"lead:{lead.Id}",
            Name = lead.Name ?? string.Empty,
            Phone = lead.Phone,
            Email = lead.Email,
            Website = lead.Website,
            Address = lead.Address,
            City = BuildCity(lead),
            Vertical = lead.Niche?.Name ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
        };

        var persisted = await _listings.UpsertListingAsync(listing, ct);
        var enricher = _enricherFactory.CreateFor(lead.Niche?.Name);
        var rollup = await enricher.EnrichAsync(persisted, forceReRun: true, ct);

        var hasSomething = !string.IsNullOrWhiteSpace(rollup.OwnerName) || !string.IsNullOrWhiteSpace(rollup.Email);
        var finalStatus = rollup.Status == ListingEnrichmentStatus.Failed
            ? EnrichStatus.Failed
            : hasSomething ? EnrichStatus.Completed : EnrichStatus.NoEmailFound;

        _logger.LogInformation(
            "LeadOwnerEnrichment: lead {LeadId} -> owner={Owner} verified={Verified} email={Email} status={Status}",
            leadId, rollup.OwnerName ?? "-", rollup.OwnerNameVerified, rollup.Email ?? "-", finalStatus);

        return new LeadEnrichmentOutcome
        {
            OwnerName = rollup.OwnerName,
            OwnerVerified = rollup.OwnerNameVerified,
            OwnerConfidence = rollup.OwnerNameConfidence,
            OwnerSources = rollup.OwnerNameSources,
            Email = rollup.Email,
            EmailStatus = (int)rollup.EmailStatus,
            FinalStatus = finalStatus,
        };
    }

    private static string BuildCity(Lead lead)
    {
        var city = lead.City?.Name;
        var state = lead.State?.Iso2;   // 2-letter state code, e.g. "CO"
        if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(state))
            return $"{city}, {state}";
        return city ?? string.Empty;
    }
}
