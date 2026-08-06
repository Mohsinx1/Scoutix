using Hangfire.Dashboard;
using Microsoft.Extensions.Configuration;

namespace Scoutix.Filters
{
    /// <summary>
    /// Gates the Hangfire dashboard at /hangfire. The allowlist is read from
    /// configuration (HangfireDashboard:AuthorizedEmails) rather than hardcoded,
    /// so operator identities are never committed to source control.
    /// An empty or missing list denies everyone.
    /// </summary>
    public class HangfireAuthFilter : IDashboardAuthorizationFilter
    {
        private readonly IConfiguration _configuration;

        public HangfireAuthFilter(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();

            if (httpContext.User?.Identity?.IsAuthenticated != true)
                return false;

            var email = httpContext.User
                .FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

            if (string.IsNullOrWhiteSpace(email))
                return false;

            var allowed = _configuration
                .GetSection("HangfireDashboard:AuthorizedEmails")
                .Get<string[]>() ?? System.Array.Empty<string>();

            return allowed.Any(a =>
                string.Equals(a, email, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
