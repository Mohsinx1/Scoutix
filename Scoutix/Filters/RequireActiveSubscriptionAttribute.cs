using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Scoutix.Models;

namespace Scoutix.Filters
{
    public class RequireActiveSubscriptionAttribute : ActionFilterAttribute
    {
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var dbContext = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            var userIdClaim = context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(userIdClaim, out int userId))
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            var subscription = await dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.IsSubscriptionActive == true);

            if (subscription == null)
            {
                var tempData = context.HttpContext.RequestServices
                    .GetRequiredService<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataDictionaryFactory>()
                    .GetTempData(context.HttpContext);

                var user = await dbContext.Users.FindAsync(userId);
                bool hasNeverSubscribed = user?.SubscriptionId == null;

                tempData["Message"] = hasNeverSubscribed
                    ? "Please complete your subscription to get started."
                    : "Your subscription has expired. Please renew to continue.";
                tempData["MessageType"] = "warning";

                context.Result = new RedirectToActionResult("ChoosePlan", "Account", null);
                return;
            }

            await next();
        }
    }
}