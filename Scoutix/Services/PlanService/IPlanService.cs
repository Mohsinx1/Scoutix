using Scoutix.Models.Entitlements;

namespace Scoutix.Services.PlanService;

public interface IPlanService
{
    // Get the plan config for a user
    Task<PlanConfig> GetUserPlanConfigAsync(int userId);

    // Check if user has remaining lead quota this billing period
    Task<bool> HasLeadQuotaAsync(int userId);

    // Get how many leads user has used this billing period
    Task<int> GetUsedLeadsThisPeriodAsync(int userId);

    // Get how many leads user has remaining this billing period
    Task<int> GetRemainingQuotaAsync(int userId);

    // Check if user's plan includes a specific feature
    Task<bool> HasFeatureAccessAsync(int userId, Func<PlanConfig, bool> feature);

    Task<DateTime> GetCurrentPeriodStartAsync(int userId);
}