using Scoutix.Models;
using Scoutix.Models.Entitlements;
using Microsoft.EntityFrameworkCore;

namespace Scoutix.Services.PlanService;

public class PlanService : IPlanService
{
    private readonly ApplicationDbContext _context;

    public PlanService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PlanConfig> GetUserPlanConfigAsync(int userId)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.PlanId == null)
            return PlanFeatures.GetConfig(1); // default to Starter

        return PlanFeatures.GetConfig(user.PlanId.Value);
    }

    public async Task<int> GetUsedLeadsThisPeriodAsync(int userId)
    {
        var subscription = await _context.Subscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.IsSubscriptionActive == true);

        if (subscription == null)
            return 0;

        var periodStart = GetCurrentPeriodStart(subscription.StartDate ?? DateTime.UtcNow);

        return await _context.UserLeads
            .CountAsync(ul => ul.UserId == userId && ul.CreatedAt >= periodStart);
    }

    public async Task<bool> HasLeadQuotaAsync(int userId)
    {
        var config = await GetUserPlanConfigAsync(userId);
        var used = await GetUsedLeadsThisPeriodAsync(userId);
        return used < config.LeadQuota;
    }

    public async Task<int> GetRemainingQuotaAsync(int userId)
    {
        var config = await GetUserPlanConfigAsync(userId);
        var used = await GetUsedLeadsThisPeriodAsync(userId);
        return Math.Max(0, config.LeadQuota - used);
    }

    public async Task<bool> HasFeatureAccessAsync(int userId, Func<PlanConfig, bool> feature)
    {
        var config = await GetUserPlanConfigAsync(userId);
        return feature(config);
    }

    // ─────────────────────────────────────────────
    // PRIVATE — Calculate current billing period start
    // Rolls forward from subscription start date
    // month by month until we reach current period
    // ─────────────────────────────────────────────
    private static DateTime GetCurrentPeriodStart(DateTime subscriptionStart)
    {
        var today = DateTime.UtcNow;
        var periodStart = subscriptionStart;

        while (periodStart.AddMonths(1) <= today)
            periodStart = periodStart.AddMonths(1);

        return periodStart;
    }
    public async Task<DateTime> GetCurrentPeriodStartAsync(int userId)
    {
        var subscription = await _context.Subscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.IsSubscriptionActive == true);

        if (subscription == null)
            return DateTime.UtcNow;

        return GetCurrentPeriodStart(subscription.StartDate ?? DateTime.UtcNow);
    }
}