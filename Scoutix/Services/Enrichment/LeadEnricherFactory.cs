using Scoutix.Enrichment;
using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Email;
using Scoutix.Enrichment.Sources;
using Scoutix.Enrichment.Sources.CoBoard;
using Scoutix.Enrichment.Sources.Npi;
using Scoutix.Enrichment.Sources.Website;

namespace Scoutix.Services.Enrichment;

public interface ILeadEnricherFactory
{
    /// <summary>Builds an enricher with the owner sources appropriate for the lead's vertical.</summary>
    ListingEnricher CreateFor(string? nicheName);
}

/// <summary>
/// Picks owner sources per vertical. Website owner-finding runs for every niche; the registry
/// sources (NPI + Colorado dental board) only fire for dentist leads — the validated vertical.
/// Adding a vertical = add its resolver and gate it here; no other code changes.
/// </summary>
public sealed class LeadEnricherFactory : ILeadEnricherFactory
{
    private readonly WebsiteOwnerSource _website;
    private readonly NpiRegistrySource _npi;
    private readonly ColoradoDentalBoardSource _board;
    private readonly ICandidateStore _candidates;
    private readonly IListingStore _listings;
    private readonly IEmailStage _emailStage;
    private readonly ILogger<ListingEnricher> _logger;

    public LeadEnricherFactory(
        WebsiteOwnerSource website, NpiRegistrySource npi, ColoradoDentalBoardSource board,
        ICandidateStore candidates, IListingStore listings, IEmailStage emailStage,
        ILogger<ListingEnricher> logger)
    {
        _website = website;
        _npi = npi;
        _board = board;
        _candidates = candidates;
        _listings = listings;
        _emailStage = emailStage;
        _logger = logger;
    }

    public ListingEnricher CreateFor(string? nicheName)
    {
        var sources = new List<IOwnerNameSource> { _website };
        if (EnrichmentVerticals.IsDentistry(nicheName))
        {
            sources.Add(_npi);
            sources.Add(_board);
        }
        return new ListingEnricher(sources, _candidates, _listings, _logger, _emailStage);
    }
}
