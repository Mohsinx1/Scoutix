using Scoutix.Models;
using Microsoft.EntityFrameworkCore;

namespace Scoutix.Jobs
{
    public class ExpiredSubscriptionJob
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ExpiredSubscriptionJob> _logger;

        public ExpiredSubscriptionJob(ApplicationDbContext context, ILogger<ExpiredSubscriptionJob> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task RevokeExpiredSubscriptions()
        {
            var now = DateTime.UtcNow;

            var expiredSubscriptions = await _context.Subscriptions
                .Where(s => s.IsSubscriptionActive == true && s.EndDate < now)
                .ToListAsync();

            if (!expiredSubscriptions.Any())
            {
                _logger.LogInformation("ExpiredSubscriptionJob: no expired subscriptions found.");
                return;
            }

            foreach (var subscription in expiredSubscriptions)
            {
                subscription.IsSubscriptionActive = false;
                subscription.UpdatedAt = now;
                _logger.LogInformation("ExpiredSubscriptionJob: revoked access for subscription {SubId}, UserId {UserId}",
                    subscription.Id, subscription.UserId);
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("ExpiredSubscriptionJob: revoked {Count} expired subscriptions.", expiredSubscriptions.Count);
        }
    }
}