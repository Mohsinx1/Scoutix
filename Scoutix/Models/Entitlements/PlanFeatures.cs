namespace Scoutix.Models.Entitlements
{
    public static class PlanFeatures
    {
        public static readonly Dictionary<int, PlanConfig> Plans = new()
        {
            [1] = new PlanConfig  // Starter
            {
                PlanId = 1,
                PlanName = "Starter",
                LeadQuota = 800,
                MaxLeadsPerSearch = 50,
                MaxGeographyScope = GeographyScope.City,
                ScrapingDelayMs = 4000,
                HangfireQueue = "starter",
                CanExportCsv = false,
                CanUseAdvancedFilters = false,
                FollowUpReminders = false,
                CanEnrich = true,
                QueuePriority = 1
            },
            [2] = new PlanConfig  // Growth
            {
                PlanId = 2,
                PlanName = "Growth",
                LeadQuota = 3000,
                MaxLeadsPerSearch = 200,
                MaxGeographyScope = GeographyScope.State,
                ScrapingDelayMs = 1500,
                HangfireQueue = "growth",
                CanExportCsv = true,
                CanUseAdvancedFilters = true,
                FollowUpReminders = true,
                CanEnrich = true,
                QueuePriority = 2
            },
            [3] = new PlanConfig  // Pro
            {
                PlanId = 3,
                PlanName = "Pro",
                LeadQuota = 8000,
                MaxLeadsPerSearch = 500,
                MaxGeographyScope = GeographyScope.Country,
                ScrapingDelayMs = 0,
                HangfireQueue = "pro",
                CanExportCsv = true,
                CanUseAdvancedFilters = true,
                FollowUpReminders = true,
                CanEnrich = true,
                QueuePriority = 3
            }
        };

        public static PlanConfig GetConfig(int planId)
        {
            if (Plans.TryGetValue(planId, out var config))
                return config;
            return Plans[1];
        }
    }
}