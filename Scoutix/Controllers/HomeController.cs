using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scoutix.Models;
using Scoutix.Services.EmailService;
using System.Configuration;

namespace Scoutix.Controllers
{
    [AllowAnonymous]
    public class HomeController : Controller
    {
        private readonly IEmailService _emailService;
        public HomeController(IEmailService emailService)
        {
            _emailService = emailService;
        }
        public IActionResult Index()
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("ManageLeads", "Leads");
            }
            ViewData["Title"] = "Lead Intelligence Platform for Local Business Prospecting";
            ViewData["Description"] = "Scoutix discovers verified local business prospects, enriches them with email addresses, and manages your entire pipeline in a built-in CRM. Start closing more deals from $39/month.";
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Contact(string name, string email, string subject, string message)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email)
                || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(message))
            {
                return Json(new { success = false, message = "All fields are required." });
            }

            await _emailService.SendContactFormEmailAsync(name, email, subject, message);
            return Json(new { success = true });
        }
    }
}
    