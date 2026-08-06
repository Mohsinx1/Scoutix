namespace Scoutix.Services.Enrichment;

/// <summary>
/// Which verticals the registry sources cover — shared by the enricher factory (source selection)
/// and the controller (so dentist leads with no website still enqueue, since the registry can find
/// their owner from name + phone). Add a vertical here when its resolver is wired.
/// </summary>
public static class EnrichmentVerticals
{
    public static bool IsDentistry(string? nicheName) =>
        nicheName is not null &&
        (nicheName.Contains("dentist", StringComparison.OrdinalIgnoreCase) ||
         nicheName.Contains("dental", StringComparison.OrdinalIgnoreCase));
}
