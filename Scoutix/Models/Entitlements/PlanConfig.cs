namespace Scoutix.Models.Entitlements
{
    public enum GeographyScope
    {
        City = 1,
        State = 2,
        Country = 3
    }

    public class PlanConfig
    {
        public int PlanId { get; set; }
        public string PlanName { get; set; } = null!;
        public int LeadQuota { get; set; }
        public int MaxLeadsPerSearch { get; set; }
        public GeographyScope MaxGeographyScope { get; set; }
        public int ScrapingDelayMs { get; set; }
        public string HangfireQueue { get; set; } = "starter";
        public bool CanExportCsv { get; set; }
        public bool CanUseAdvancedFilters { get; set; }
        public bool FollowUpReminders { get; set; }
        public bool CanEnrich { get; set; }
        public int QueuePriority { get; set; }
    }
}