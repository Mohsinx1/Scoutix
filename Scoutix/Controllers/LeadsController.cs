using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Scoutix.Filters;
using Scoutix.Models;
using Scoutix.Models.DTOs;
using Scoutix.Models.Entitlements;
using Scoutix.Models.Enums;
using Scoutix.Models.ViewModels;
using Scoutix.Jobs;
using Scoutix.Services.Enrichment;
using Scoutix.Services.GeoCache;
using Scoutix.Services.PlanService;
using Scoutix.Services.Scraping;
using System.Collections;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Scoutix.Controllers
{
    [RequireActiveSubscription]
    public class LeadsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LeadsController> _logger;
        private readonly IGoogleMapsScraper _googleMapsScraper;
        private readonly IGeoCache _geoCache;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IPlanService _planService;
        private readonly IBackgroundJobClient _backgroundJobClient;

        public LeadsController(ApplicationDbContext context, ILogger<LeadsController> logger,
            IGoogleMapsScraper googleMapsScraper, IGeoCache geoCache, IConfiguration configuration,
            IHttpClientFactory httpClientFactory, IPlanService planService,
            IBackgroundJobClient backgroundJobClient)
        {
            _context = context;
            _logger = logger;
            _googleMapsScraper = googleMapsScraper;
            _geoCache = geoCache;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _planService = planService;
            _backgroundJobClient = backgroundJobClient;
        }

        public async Task<IActionResult> Dashboard()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return RedirectToAction("Login", "Account");

            // ── User + Subscription ──
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userId);

            var subscription = await _context.Subscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.UserId == userId && s.IsSubscriptionActive == true);

            if (subscription == null)
                return RedirectToAction("ChoosePlan", "Account");

            // ── Billing Period ──
            var periodStart = await _planService.GetCurrentPeriodStartAsync(userId);
            var periodEnd = periodStart.AddMonths(1);
            var daysUntilReset = (periodEnd - DateTime.UtcNow).Days;

            // ── Quota ──
            var remaining = await _planService.GetRemainingQuotaAsync(userId);
            var used = await _planService.GetUsedLeadsThisPeriodAsync(userId);
            var quota = await _planService.GetUserPlanConfigAsync(userId);
            var quotaPercent = quota.LeadQuota > 0
                ? Math.Round((double)used / quota.LeadQuota * 100, 1)
                : 0;

            // ── Stat Cards ──
            var totalLeads = await _context.UserLeads
                .CountAsync(ul => ul.UserId == userId);

            var closedWon = await _context.UserLeads
                .CountAsync(ul => ul.UserId == userId && ul.Status == (int)LeadStatus.Closed);

            var conversionRate = totalLeads > 0
                ? Math.Round((double)closedWon / totalLeads * 100, 1)
                : 0;

            var today = DateTime.UtcNow.Date;
            var followUpsToday = await _context.UserLeads
                .CountAsync(ul => ul.UserId == userId && ul.FollowUpDate.HasValue && ul.FollowUpDate.Value.Date == today);

            // ── Status Breakdown ──
            var statusBreakdown = await _context.UserLeads
                .Where(ul => ul.UserId == userId)
                .GroupBy(ul => ul.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            // ── Follow-up Today List ──
            var followUpList = await _context.UserLeads
                .Where(ul => ul.UserId == userId && ul.FollowUpDate.HasValue && ul.FollowUpDate.Value.Date == today)
                .OrderBy(ul => ul.FollowUpDate)
                .Take(5)
                .Include(ul => ul.Lead)
                    .ThenInclude(l => l.Niche)
                .Include(ul => ul.Lead)
                    .ThenInclude(l => l.City)
                .Select(ul => new
                {
                    Name = ul.Lead.Name,
                    City = ul.Lead.City != null ? ul.Lead.City.Name : null,
                    Status = ul.Status,
                    Niche = ul.Lead.Niche != null ? ul.Lead.Niche.Name : null
                })
                .ToListAsync();

            // ── ViewBag ──
            ViewBag.UserName = user.UserName;
            ViewBag.PlanName = subscription.Plan?.PlanName ?? "Starter";

            // Stat cards
            ViewBag.TotalLeads = totalLeads;
            ViewBag.GeneratedThisMonth = used;
            ViewBag.LeadsRemaining = remaining;
            ViewBag.ClosedWon = closedWon;
            ViewBag.ConversionRate = conversionRate;
            ViewBag.FollowUpsToday = followUpsToday;

            // Quota card
            ViewBag.LeadQuota = quota.LeadQuota;
            ViewBag.QuotaPercent = quotaPercent;
            ViewBag.DaysUntilReset = daysUntilReset;
            ViewBag.ResetDate = periodEnd.ToString("MMM d");

            // Charts and lists
            ViewBag.StatusBreakdown = statusBreakdown;
            ViewBag.FollowUpList = followUpList;

            return View();
        }
        public async Task<IActionResult> ManageLeads(LeadStatus? statusFilter, int? countryId, int? stateId, int? cityId, int? nicheId, string? search, int? clear, bool? followUpToday, string? hasWebsite)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return RedirectToAction("Login", "Account");

            if (clear == 1)
            {
                statusFilter = null;
                countryId = null;
                stateId = null;
                cityId = null;
                nicheId = null;
                search = null;
                followUpToday = null;
                hasWebsite = null;
            }

            var canUseAdvancedFilters = await _planService.HasFeatureAccessAsync(userId, p => p.CanUseAdvancedFilters);
            if (!canUseAdvancedFilters)
            {
                countryId = null;
                stateId = null;
                cityId = null;
                nicheId = null;
            }

            var query = _context.UserLeads
                .Where(ul => ul.UserId == userId)
                .AsQueryable();

            if (statusFilter.HasValue)
                query = query.Where(ul => ul.Status == (int)statusFilter.Value);

            if (countryId.HasValue)
                query = query.Where(ul => ul.Lead.CountryId == countryId.Value);

            if (stateId.HasValue)
                query = query.Where(ul => ul.Lead.StateId == stateId.Value);

            if (cityId.HasValue)
                query = query.Where(ul => ul.Lead.CityId == cityId.Value);

            if (nicheId.HasValue)
                query = query.Where(ul => ul.Lead.NicheId == nicheId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(ul =>
                    (ul.Lead.Name != null && ul.Lead.Name.Contains(term)) ||
                    (ul.Lead.Phone != null && ul.Lead.Phone.Contains(term))
                );
            }

            if (followUpToday == true)
            {
                var today = DateTime.UtcNow.Date;
                query = query.Where(ul => ul.FollowUpDate.HasValue && ul.FollowUpDate.Value.Date == today);
            }

            if (hasWebsite == "yes")
                query = query.Where(ul => ul.Lead.Website != null && ul.Lead.Website != "");
            else if (hasWebsite == "no")
                query = query.Where(ul => ul.Lead.Website == null || ul.Lead.Website == "");

            ViewBag.HasWebsite = hasWebsite;

            // Single optimized query — no separate dictionary lookups
            var vmList = await query
                .OrderBy(ul => ul.Status)
                .ThenByDescending(ul => ul.CreatedAt)
                .Select(ul => new LeadListItemViewModel
                {
                    Id = ul.Lead.Id,
                    UserLeadId = ul.Id,
                    Name = ul.Lead.Name,
                    Phone = ul.Lead.Phone,
                    Email = ul.Lead.Email,
                    Website = ul.Lead.Website,
                    NicheId = ul.Lead.NicheId,
                    Status = (LeadStatus)ul.Status,
                    Notes = ul.Notes,
                    FollowUpDate = ul.FollowUpDate,
                    CreatedAt = ul.CreatedAt,
                    CountryId = ul.Lead.CountryId,
                    StateId = ul.Lead.StateId,
                    CityId = ul.Lead.CityId,
                    Niche = ul.Lead.Niche != null ? ul.Lead.Niche.Name : null,
                    CountryName = ul.Lead.Country != null ? ul.Lead.Country.Name : null,
                    StateName = ul.Lead.State != null ? ul.Lead.State.Name : null,
                    CityName = ul.Lead.City != null ? ul.Lead.City.Name : null,
                    EnrichmentStatus = ul.Lead.EnrichmentStatus,
                    EnrichmentAttempts = ul.Lead.EnrichmentAttempts,
                    OwnerName = ul.Lead.OwnerName,
                    OwnerVerified = ul.Lead.OwnerVerified,
                    EmailStatus = ul.Lead.EmailStatus,
                })
                .ToListAsync();

            ViewBag.Statuses = Enum.GetValues(typeof(LeadStatus)).Cast<LeadStatus>().ToList();
            ViewBag.StatusFilter = statusFilter;
            ViewBag.SelectedCountryId = countryId;
            ViewBag.SelectedStateId = stateId;
            ViewBag.SelectedCityId = cityId;
            ViewBag.SelectedNicheId = nicheId;
            ViewBag.Search = search;

            var canExport = await _planService.HasFeatureAccessAsync(userId, p => p.CanExportCsv);
            var user = await _context.Users.FindAsync(userId);

            if (!canExport)
            {
                ViewBag.CanExport = false;
                ViewBag.ExportLockedReason = "CSV export is available on Growth and Pro plans. Upgrade to access this feature.";
            }
            else if (user?.LastExportAt.HasValue == true && user.LastExportAt.Value > DateTime.UtcNow.AddHours(-24))
            {
                var hoursLeft = Math.Ceiling((user.LastExportAt.Value.AddHours(24) - DateTime.UtcNow).TotalHours);
                ViewBag.CanExport = false;
                ViewBag.ExportLockedReason = $"You can export once every 24 hours. Try again in {hoursLeft} hour(s).";
            }
            else
            {
                ViewBag.CanExport = true;
                ViewBag.ExportLockedReason = null;
            }

            ViewBag.CanUseFollowUpReminders = await _planService.HasFeatureAccessAsync(userId, p => p.FollowUpReminders);
            ViewBag.CanUseAdvancedFilters = canUseAdvancedFilters;

            return View(vmList);
        }


        public async Task<IActionResult> DetailsPartial(int id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            var vm = await _context.UserLeads
                .AsNoTracking()
                .Where(ul => ul.LeadId == id && ul.UserId == userId)
                .Include(ul => ul.Lead)
                .Select(ul => new LeadDetailsViewModel
                {
                    Id = ul.Lead.Id,
                    UserLeadId = ul.Id,
                    Name = ul.Lead.Name,
                    Phone = ul.Lead.Phone,
                    Email = ul.Lead.Email,
                    Website = ul.Lead.Website,
                    Address = ul.Lead.Address,
                    Source = ul.Lead.Source,
                    Status = (LeadStatus)ul.Status,
                    Notes = ul.Notes,
                    FollowUpDate = ul.FollowUpDate,
                    CreatedAt = ul.CreatedAt,
                    UpdatedAt = ul.UpdatedAt,
                    CountryName = ul.Lead.Country != null ? ul.Lead.Country.Name : null,
                    StateName = ul.Lead.State != null ? ul.Lead.State.Name : null,
                    CityName = ul.Lead.City != null ? ul.Lead.City.Name : null,
                    Niche = ul.Lead.Niche != null ? ul.Lead.Niche.Name : null
                })
                .FirstOrDefaultAsync();

            if (vm == null)
                return NotFound();

            return PartialView("_LeadDetailsDrawer", vm);
        }



        [HttpPost]
        public async Task<IActionResult> UpdateLeadStatus([FromBody] UpdateLeadStatus dto)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            var userLead = await _context.UserLeads
                .FirstOrDefaultAsync(ul => ul.LeadId == dto.UserLeadId && ul.UserId == userId);

            if (userLead == null) return NotFound();

            userLead.Status = dto.Status;

            if (!string.IsNullOrWhiteSpace(dto.Notes))
                userLead.Notes = dto.Notes;

            // Only set follow-up date for FollowUp status and if plan allows it
            if (dto.Status == (int)LeadStatus.FollowUp && dto.FollowUpDate.HasValue)
            {
                var canUseFollowUp = await _planService.HasFeatureAccessAsync(userId, p => p.FollowUpReminders);
                if (canUseFollowUp)
                    userLead.FollowUpDate = dto.FollowUpDate.Value;
            }
            else if (dto.Status != (int)LeadStatus.FollowUp)
            {
                // Clear follow-up date if status changed away from FollowUp
                userLead.FollowUpDate = null;
            }

            userLead.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> GetJobStatus()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Json(new { hasActiveJob = false, newLeads = 0, status = "unknown" });

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return Json(new { hasActiveJob = false, newLeads = 0, status = "unknown" });

            // Calculate newLeads regardless of job state
            var currentCount = await _context.UserLeads.CountAsync(ul => ul.UserId == userId);
            var newLeads = Math.Max(0, currentCount - user.LeadGenBaseCount);

            if (string.IsNullOrEmpty(user.ActiveJobId))
                return Json(new { hasActiveJob = false, newLeads, status = "none" });

            var job = JobStorage.Current.GetMonitoringApi().JobDetails(user.ActiveJobId);
            var lastState = job?.History.FirstOrDefault()?.StateName ?? "unknown";

            if (lastState == "Succeeded" || lastState == "Failed" || lastState == "Deleted")
            {
                user.ActiveJobId = null;
                await _context.SaveChangesAsync();

                return Json(new
                {
                    hasActiveJob = false,
                    newLeads,
                    status = lastState.ToLower()
                });
            }

            return Json(new
            {
                hasActiveJob = true,
                newLeads,
                status = lastState.ToLower()
            });
        }
        private bool IsComboBeingScrapped(int nicheId, int countryId, int? stateId, int? cityId)
        {
            return _context.NicheGeoProgresses.Any(n =>
                n.NicheId == nicheId &&
                n.CountryId == countryId &&
                n.StateId == stateId &&
                n.CityId == cityId &&
                n.IsInProgress == true &&
                n.IsExhausted == false);
        }

        [HttpGet]
        public async Task<IActionResult> GenerateLeads()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return RedirectToAction("Login", "Account");

            var planConfig = await _planService.GetUserPlanConfigAsync(userId);
            ViewBag.MaxGeographyScope = planConfig.MaxGeographyScope.ToString();
            ViewBag.PlanName = planConfig.PlanName;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateLeads(
     int nicheId,
     int reqLeads,
     int countryId,
     int? stateId,
     int? cityId)
        {
            // Input validation
            if (nicheId <= 0)
            {
                TempData["Message"] = "Please select a niche.";
                TempData["MessageType"] = "error";
                return RedirectToAction("GenerateLeads");
            }
            if (countryId <= 0)
            {
                TempData["Message"] = "Please select a country.";
                TempData["MessageType"] = "error";
                return RedirectToAction("GenerateLeads");
            }
            if (reqLeads <= 0 || reqLeads > 500)
            {
                TempData["Message"] = "Please enter a number of leads between 1 and 500.";
                TempData["MessageType"] = "error";
                return RedirectToAction("GenerateLeads");
            }
            // Auth
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
            {
                TempData["Message"] = "Session error. Please log in again.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Login", "Account");
            }

            // Block if job already running
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                TempData["Message"] = "User not found.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Logout", "Account");
            }
            if (!string.IsNullOrEmpty(user.ActiveJobId))
            {
                TempData["Message"] = "You already have a lead generation job running. Please wait for it to finish.";
                TempData["MessageType"] = "warning";
                return RedirectToAction("GenerateLeads");
            }

            // Quota check
            var remaining = await _planService.GetRemainingQuotaAsync(userId);
            if (remaining <= 0)
            {
                TempData["Message"] = "You've reached your monthly lead limit. Upgrade your plan to generate more leads.";
                TempData["MessageType"] = "warning";
                return RedirectToAction("GenerateLeads");
            }

            // Get plan config — needed for enforcement checks below
            var planConfig = await _planService.GetUserPlanConfigAsync(userId);

            // Cap to quota
            bool wasQuotaCapped = false;
            if (reqLeads > remaining)
            {
                reqLeads = remaining;
                wasQuotaCapped = true;
            }

            // Max leads per search enforcement
            bool wasSearchCapped = false;
            if (reqLeads > planConfig.MaxLeadsPerSearch)
            {
                reqLeads = planConfig.MaxLeadsPerSearch;
                wasSearchCapped = true;
            }

            // Geography scope enforcement
            if (planConfig.MaxGeographyScope == GeographyScope.City && cityId == null)
            {
                TempData["Message"] = $"Your {planConfig.PlanName} plan only supports city-level searches. Please select a city or upgrade to Growth for state-level searches.";
                TempData["MessageType"] = "warning";
                return RedirectToAction("GenerateLeads");
            }

            if (planConfig.MaxGeographyScope == GeographyScope.State && stateId == null)
            {
                TempData["Message"] = $"Your {planConfig.PlanName} plan only supports up to state-level searches. Please select a state or upgrade to Pro for country-level searches.";
                TempData["MessageType"] = "warning";
                return RedirectToAction("GenerateLeads");
            }

            // Check available leads in DB
            var availableLeads = await GetAvailableLeadsAsync(userId, nicheId, countryId, stateId, cityId);
            if (availableLeads >= reqLeads)
            {
                var assigned = await AssignLeadsToUserAsync(userId, nicheId, countryId, stateId, cityId, reqLeads);
                var msg = wasQuotaCapped
                    ? $"{assigned} leads added to your account. (Capped to your remaining quota)"
                    : $"{assigned} leads added to your account.";

                TempData["Message"] = msg;
                TempData["MessageType"] = "success";
                return RedirectToAction("GenerateLeads");
            }

            // Need background job
            var leadsToGenerate = reqLeads - availableLeads;

            if (availableLeads > 0)
            {
                var assigned = await AssignLeadsToUserAsync(userId, nicheId, countryId, stateId, cityId, availableLeads);
                leadsToGenerate = reqLeads - assigned;
            }

            // Check if combo is already being scraped
            if (IsComboBeingScrapped(nicheId, countryId, stateId, cityId))
            {
                var busyMessage = availableLeads > 0
                    ? $"{reqLeads - leadsToGenerate} leads added to your account. More leads for this location are currently being generated. Request again in a few minutes to get more."
                    : "Leads for this location are currently being generated. Request again in a few minutes.";

                if (wasQuotaCapped)
                    busyMessage += $" Request capped to {reqLeads} leads based on your remaining quota.";

                TempData["Message"] = busyMessage;
                TempData["MessageType"] = "info";
                return RedirectToAction("GenerateLeads");
            }
            var hasBeenScraped = _context.NicheGeoProgresses.Any(n =>
    n.NicheId == nicheId &&
    n.CountryId == countryId &&
    (stateId == null || n.StateId == stateId) &&
    (cityId == null || n.CityId == cityId));

            if (hasBeenScraped)
            {
                var allExhausted = !_context.NicheGeoProgresses.Any(n =>
                    n.NicheId == nicheId &&
                    n.CountryId == countryId &&
                    (stateId == null || n.StateId == stateId) &&
                    (cityId == null || n.CityId == cityId) &&
                    n.IsExhausted == false);

                if (allExhausted)
                {
                    var nichName = (await _context.Niches.FindAsync(nicheId))?.Name ?? "leads";
                    var countryName = (await _context.Countries.FindAsync(countryId))?.Name ?? "this location";
                    var locationName = cityId.HasValue
                        ? (await _context.Cities.FindAsync(cityId.Value))?.Name ?? countryName
                        : stateId.HasValue
                            ? (await _context.States.FindAsync(stateId.Value))?.Name ?? countryName
                            : countryName;

                    TempData["Message"] = $"All {nichName.ToLower()} in {locationName} have already been generated. Try a different location or niche.";
                    TempData["MessageType"] = "warning";
                    return RedirectToAction("GenerateLeads");
                }
            }
            // Capture base count AFTER instant leads are assigned
            var baseCount = await _context.UserLeads.CountAsync(ul => ul.UserId == userId);

            var jobId = BackgroundJob.Enqueue(planConfig.HangfireQueue,
            () => GenerateLeadsBackground(nicheId, leadsToGenerate, countryId, stateId, cityId, userId));

            user.ActiveJobId = jobId;
            user.LeadGenBaseCount = baseCount;
            await _context.SaveChangesAsync();

            var message = availableLeads > 0
                ? $"{reqLeads - leadsToGenerate} leads added instantly. Generating {leadsToGenerate} more in the background."
                : "Generating leads in the background. You can continue working.";

            if (wasQuotaCapped && wasSearchCapped)
                message += $" Request capped to {reqLeads} leads based on your plan limit and remaining quota.";
            else if (wasQuotaCapped)
                message += $" Request capped to {reqLeads} leads based on your remaining quota.";
            else if (wasSearchCapped)
                message += $" Request capped to {planConfig.MaxLeadsPerSearch} leads based on your {planConfig.PlanName} plan limit.";

            TempData["Message"] = message;
            TempData["MessageType"] = "info";

            return RedirectToAction("GenerateLeads");
        }
        private async Task<int> AssignLeadsToUserAsync(
    int userId,
    int nicheId,
    int countryId,
    int? stateId,
    int? cityId,
    int count)
        {
            // Get leads matching criteria that user doesn't already have
            var alreadyOwnedLeadIds = await _context.UserLeads
                .Where(ul => ul.UserId == userId)
                .Select(ul => ul.LeadId)
                .ToListAsync();

            var leads = await _context.Leads
                .Where(l =>
                    l.NicheId == nicheId &&
                    l.CountryId == countryId &&
                    (stateId == null || l.StateId == stateId) &&
                    (cityId == null || l.CityId == cityId) &&
                    !alreadyOwnedLeadIds.Contains(l.Id))
                .Take(count)
                .ToListAsync();

            if (!leads.Any())
                return 0;

            var userLeads = leads.Select(l => new UserLead
            {
                UserId = userId,
                LeadId = l.Id,
                Status = 0,
                CreatedAt = DateTime.UtcNow
            }).ToList();

            await _context.UserLeads.AddRangeAsync(userLeads);
            await _context.SaveChangesAsync();

            return userLeads.Count;
        }
        private async Task<int> GetAvailableLeadsAsync(int userId, int nicheId, int countryId, int? stateId, int? cityId)
        {
            var alreadyOwnedLeadIds = await _context.UserLeads
                .Where(ul => ul.UserId == userId)
                .Select(ul => ul.LeadId)
                .ToListAsync();

            return await _context.Leads
                .Where(l =>
                    l.NicheId == nicheId &&
                    l.CountryId == countryId &&
                    (stateId == null || l.StateId == stateId) &&
                    (cityId == null || l.CityId == cityId) &&
                    !alreadyOwnedLeadIds.Contains(l.Id))
                .CountAsync();
        }

        public async Task GenerateLeadsBackground(
      int nicheId,
      int reqLeads,
      int countryId,
      int? stateId,
      int? cityId,
      int userId)
        {
            var jobId = Guid.NewGuid().ToString("N");
            var sw = Stopwatch.StartNew();

            try
            {
                _logger.LogInformation(
                    $"LeadGen JobStarted | JobId={jobId} | NicheId={nicheId} | ReqLeads={reqLeads} | CountryId={countryId} | StateId={stateId} | CityId={cityId}");

                Nich nich = await _context.Niches.FindAsync(nicheId);
                if (nich == null)
                {
                    sw.Stop();
                    _logger.LogWarning(
                        $"LeadGen JobFailed | JobId={jobId} | Reason=NicheNotFound | NicheId={nicheId} | " +
                        $"ElapsedMinutes={sw.Elapsed.TotalMinutes:F2} | ElapsedSeconds={sw.Elapsed.TotalSeconds:F2} | ElapsedMs={sw.ElapsedMilliseconds}");
                    return;
                }

                Country? country = await _context.Countries.FindAsync(countryId);
                if (country == null)
                {
                    sw.Stop();
                    _logger.LogWarning(
                        $"LeadGen JobFailed | JobId={jobId} | Reason=CountryNotFound | CountryId={countryId} | " +
                        $"ElapsedMinutes={sw.Elapsed.TotalMinutes:F2} | ElapsedSeconds={sw.Elapsed.TotalSeconds:F2} | ElapsedMs={sw.ElapsedMilliseconds}");
                    return;
                }

                State? state = null;
                City? city = null;

                if (stateId.HasValue)
                    state = await _context.States.FindAsync(stateId.Value);

                if (cityId.HasValue)
                    city = await _context.Cities.FindAsync(cityId.Value);

                var locationParts = new List<string>();
                if (city != null) locationParts.Add(city.Name);
                if (state != null) locationParts.Add(state.Name);
                if (country != null) locationParts.Add(country.Name);

                var locationString = string.Join(", ", locationParts);

                if (string.IsNullOrWhiteSpace(locationString))
                {
                    sw.Stop();
                    _logger.LogWarning(
                        $"LeadGen JobFailed | JobId={jobId} | Reason=LocationMissing | " +
                        $"ElapsedMinutes={sw.Elapsed.TotalMinutes:F2} | ElapsedSeconds={sw.Elapsed.TotalSeconds:F2} | ElapsedMs={sw.ElapsedMilliseconds}");
                    return;
                }

                var existingPhones = await _context.Leads
                    .Where(m => m.NicheId == nicheId)
                    .Select(l => l.Phone)
                    .ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

                // GenerateQueries now returns List<LeadGenQuery>
                var queries = GenerateQueries(
                    nicheId, nich.Name, country, state, city, reqLeads);
                if (!queries.Any())
                {
                    sw.Stop();
                    _logger.LogInformation(
                        $"LeadGen JobSkipped | JobId={jobId} | Reason=AllCombosExhausted | " +
                        $"NicheId={nicheId} | Location={locationString} | " +
                        $"ElapsedMs={sw.ElapsedMilliseconds}");
                    return;
                }
                _logger.LogInformation(
                    $"LeadGen ScrapeStarting | JobId={jobId} | QueriesCount={queries.Count} | Location={locationString}");

                // Updated call — no more country/state/city params
                var queryExhaustionMap = await _googleMapsScraper.ScrapeAndSaveLeadsAsync(
                    nich.Id, nich.Name, queries, reqLeads, existingPhones, userId);

                // Update NicheGeoProgress based on actual scraper results
                // KEY FIX: use each query's own location IDs, not job-level
                foreach (var query in queries)
                {
                    var isExhausted = queryExhaustionMap.ContainsKey(query.Query) && queryExhaustionMap[query.Query];

                    var row = await _context.NicheGeoProgresses
                        .FirstOrDefaultAsync(n =>
                            n.NicheId == nicheId &&
                            n.CountryId == query.CountryId &&
                            n.StateId == query.StateId &&
                            n.CityId == query.CityId &&
                            n.IsInProgress == true);

                    if (row == null) continue;

                    if (isExhausted)
                    {
                        row.IsExhausted = true;
                        row.IsInProgress = false;
                        row.LastScrapedAt = DateTime.UtcNow;
                        _logger.LogInformation($"Query exhausted: {query.Query}");
                    }
                    else
                    {
                        row.IsExhausted = false;
                        row.IsInProgress = false;
                        row.LastScrapedAt = DateTime.UtcNow;
                        _logger.LogInformation($"Query has potential: {query.Query}");
                    }
                }

                await _context.SaveChangesAsync();

                sw.Stop();
                _logger.LogInformation(
                    $"LeadGen JobSucceeded | JobId={jobId} | " +
                    $"ElapsedMinutes={sw.Elapsed.TotalMinutes:F2} | " +
                    $"ElapsedSeconds={sw.Elapsed.TotalSeconds:F2} | " +
                    $"ElapsedMs={sw.ElapsedMilliseconds}");
            }
            catch (Exception ex)
            {
                sw.Stop();

                _logger.LogError(ex,
                    $"LeadGen JobCrashed | JobId={jobId} | " +
                    $"ElapsedMinutes={sw.Elapsed.TotalMinutes:F2} | " +
                    $"ElapsedSeconds={sw.Elapsed.TotalSeconds:F2} | " +
                    $"ElapsedMs={sw.ElapsedMilliseconds}");

                throw;
            }
        }




        private List<LeadGenQuery> GenerateQueries(
       int nicheId,
       string niche,
       Country country,
       State? state,
       City? city,
       int requiredLeads)
        {
            var queries = new List<LeadGenQuery>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int estimatedLeadsPerQuery = 10;
            int neededQueries = Math.Max(3, (int)Math.Ceiling((double)requiredLeads / estimatedLeadsPerQuery));

            void AddQuery(string q, int qNicheId, Country qCountry, State qState, City qCity)
            {
                if (used.Contains(q)) return;

                int stateId = qState.Id;
                int cityId = qCity.Id;

                var existingRow = _context.NicheGeoProgresses.Local
                    .FirstOrDefault(n =>
                        n.NicheId == qNicheId &&
                        n.CountryId == qCountry.Id &&
                        n.StateId == stateId &&
                        n.CityId == cityId)
                    ?? _context.NicheGeoProgresses.FirstOrDefault(n =>
                        n.NicheId == qNicheId &&
                        n.CountryId == qCountry.Id &&
                        n.StateId == stateId &&
                        n.CityId == cityId);

                if (existingRow != null)
                {
                    if (existingRow.IsExhausted == true) return;
                    if (existingRow.IsInProgress == true) return;

                    used.Add(q);
                    queries.Add(new LeadGenQuery
                    {
                        Query = q,
                        CountryId = qCountry.Id,
                        StateId = stateId,
                        CityId = cityId,
                        CountryIso2 = qCountry.Iso2
                    });
                    existingRow.IsInProgress = true;
                    existingRow.LockedAt = null;
                    existingRow.CreatedAt = DateTime.UtcNow;
                    return;
                }

                used.Add(q);
                queries.Add(new LeadGenQuery
                {
                    Query = q,
                    CountryId = qCountry.Id,
                    StateId = stateId,
                    CityId = cityId,
                    CountryIso2 = qCountry.Iso2
                });
                _context.NicheGeoProgresses.Add(new NicheGeoProgress
                {
                    NicheId = qNicheId,
                    CountryId = qCountry.Id,
                    StateId = stateId,
                    CityId = cityId,
                    IsExhausted = false,
                    IsInProgress = true,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            bool isCityScope = city != null && state != null;
            bool isStateScope = state != null && city == null;
            bool isCountryScope = state == null && city == null;

            // =========================
            // CITY SCOPE
            // =========================
            if (isCityScope)
            {
                string baseLocation = $"{city!.Name}, {state!.Name}, {country.Name}";

                AddQuery($"{niche} in {baseLocation}", nicheId, country, state, city);

                var modifiers = new[] { "best", "top", "local", "professional", "certified", "licensed", "affordable", "reliable", "experienced", "trusted" };
                foreach (var mod in modifiers)
                {
                    if (queries.Count >= neededQueries) break;
                    AddQuery($"{mod} {niche} in {baseLocation}", nicheId, country, state, city);
                }

                var areas = new[] { "downtown", "north", "south", "east", "west" };
                foreach (var area in areas)
                {
                    if (queries.Count >= neededQueries) break;
                    AddQuery($"{niche} in {area} {city.Name}, {state.Name}, {country.Name}", nicheId, country, state, city);
                }

                // Expand to sibling cities in the same state
                var stateCities = _geoCache.GetCitiesByState(state.Id)
                    .Where(c => c.Id != city.Id);

                foreach (var c in stateCities)
                {
                    if (queries.Count >= neededQueries) break;
                    AddQuery($"{niche} in {c.Name}, {state.Name}, {country.Name}", nicheId, country, state, c);
                }
            }

            // =========================
            // STATE SCOPE — always resolve to city-level queries
            // =========================
            else if (isStateScope)
            {
                var stateCities = _geoCache.GetCitiesByState(state!.Id);

                foreach (var c in stateCities)
                {
                    if (queries.Count >= neededQueries) break;
                    AddQuery($"{niche} in {c.Name}, {state.Name}, {country.Name}", nicheId, country, state, c);
                }
            }

            // =========================
            // COUNTRY SCOPE — always resolve to city-level queries
            // =========================
            else if (isCountryScope)
            {
                var states = _geoCache.GetStatesByCountry(country.Id);

                foreach (var s in states)
                {
                    if (queries.Count >= neededQueries) break;

                    var cities = _geoCache.GetCitiesByState(s.Id);

                    foreach (var c in cities)
                    {
                        if (queries.Count >= neededQueries) break;
                        AddQuery($"{niche} in {c.Name}, {s.Name}, {country.Name}", nicheId, country, s, c);
                    }
                }
            }

            _context.SaveChanges();

            return queries;
        }

        public async Task<IActionResult> Settings()
        {
            // Get current logged in user from claims
            var email = User.FindFirstValue(ClaimTypes.Email);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null) return RedirectToAction("Login", "Account");

            // Account info
            ViewBag.UserName = user.UserName;
            ViewBag.UserEmail = user.Email;

            // Get subscription with plan details
            var subscription = await _context.Subscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.UserId == user.Id);

            if (subscription != null)
            {
                ViewBag.PlanName = subscription.Plan.PlanName;
                ViewBag.PlanPrice = $"${subscription.Plan.Price:F2}";
                ViewBag.SubscriptionStatus = subscription.SubscriptionStatus; // active, canceled, paused, past_due
                ViewBag.StartDate = subscription.StartDate?.ToString("MMM dd, yyyy") ?? "—";
                ViewBag.EndDate = subscription.EndDate?.ToString("MMM dd, yyyy") ?? "—";
            }
            else
            {
                ViewBag.PlanName = "No Plan";
                ViewBag.PlanPrice = "$0.00";
                ViewBag.SubscriptionStatus = "canceled";
                ViewBag.StartDate = "—";
                ViewBag.EndDate = "—";
            }
            return View();
        }
        [HttpPost]
        [Route("Leads/BillingPortal")]
        public async Task<IActionResult> BillingPortal()
        {
            var email = User.FindFirstValue(ClaimTypes.Email);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null || string.IsNullOrEmpty(user.PaddleCustomerId))
            {
                TempData["Message"] = "Billing portal unavailable. Please contact support.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Settings");
            }

            var apiKey = _configuration["Paddle:ApiKey"];
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var paddleBaseUrl = _configuration["Paddle:BaseUrl"]?.TrimEnd('/');
            var response = await httpClient.PostAsJsonAsync(
                $"{paddleBaseUrl}/customers/{user.PaddleCustomerId}/portal-sessions",
                new { }
            );

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Paddle portal session failed. Status: {Status}, Body: {Body}",
                    response.StatusCode, errorBody);
                TempData["Message"] = "Could not open billing portal. Please try again.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Settings");
            }

            var json = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject<dynamic>(json);
            string portalUrl = result.data.urls.general.overview?.ToString();

            return Redirect(portalUrl);
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePlan(string newPlanName)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
            {
                TempData["Message"] = "Session error. Please log in again.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Settings", "Leads");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user?.SubscriptionId == null || user.PlanId == null)
            {
                TempData["Message"] = "No active subscription found.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Settings", "Leads");
            }

            var subscription = await _context.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == user.SubscriptionId);

            if (subscription == null || string.IsNullOrEmpty(subscription.PaddleSubscriptionId))
            {
                TempData["Message"] = "Could not load subscription details.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Settings", "Leads");
            }

            var newPlan = await _context.Plans.FirstOrDefaultAsync(p => p.PlanName == newPlanName);
            if (newPlan == null || newPlan.Id == user.PlanId)
            {
                TempData["Message"] = "Invalid plan selected.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Settings", "Leads");
            }

            var planHierarchy = new Dictionary<string, int>
    {
        { "Starter", 1 }, { "Growth", 2 }, { "Pro", 3 }
    };

            var currentPlan = await _context.Plans.FirstOrDefaultAsync(p => p.Id == user.PlanId);
            planHierarchy.TryGetValue(currentPlan?.PlanName ?? "", out int currentLevel);
            planHierarchy.TryGetValue(newPlanName, out int newLevel);

            bool isUpgrade = newLevel > currentLevel;
            string prorationBillingMode = isUpgrade ? "prorated_immediately" : "prorated_next_billing_period";

            var paddleApiKey = _configuration["Paddle:ApiKey"];
            var paddleBaseUrl = _configuration["Paddle:BaseUrl"]?.TrimEnd('/');
            var apiUrl = $"{paddleBaseUrl}/subscriptions/{subscription.PaddleSubscriptionId}";
            var payload = new
            {
                items = new[] { new { price_id = newPlan.PaddlePriceId, quantity = 1 } },
                proration_billing_mode = prorationBillingMode
            };

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", paddleApiKey);

            var request = new HttpRequestMessage(HttpMethod.Patch, apiUrl)
            {
                Content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Network error calling Paddle ChangePlan for user {UserId}", userId);
                TempData["Message"] = "Network error contacting payment provider. Please try again.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Settings", "Leads");
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Paddle ChangePlan failed. Status: {StatusCode}, Body: {Body}",
                    response.StatusCode, errorBody);
                TempData["Message"] = "Failed to update your plan. Please try again or contact support.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Settings", "Leads");
            }

            TempData["Message"] = isUpgrade
                ? $"You've been upgraded to {newPlan.PlanName}. Changes are effective immediately."
                : $"Your plan will change to {newPlan.PlanName} at the start of your next billing period.";
            TempData["MessageType"] = "success";

            return RedirectToAction("Settings", "Leads");
        }
        [HttpGet]
        public async Task<IActionResult> ExportCsv()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return RedirectToAction("Login", "Account");

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return RedirectToAction("Logout", "Account");

            // Plan gate — Growth and Pro only
            var canExport = await _planService.HasFeatureAccessAsync(userId, p => p.CanExportCsv);
            if (!canExport)
            {
                TempData["Message"] = "CSV export is available on Growth and Pro plans. Upgrade to access this feature.";
                TempData["MessageType"] = "warning";
                return RedirectToAction("ManageLeads");
            }

            // Rate limit — 1 export per 24 hours
            if (user.LastExportAt.HasValue && user.LastExportAt.Value > DateTime.UtcNow.AddHours(-24))
            {
                var nextExport = user.LastExportAt.Value.AddHours(24);
                var hoursLeft = Math.Ceiling((nextExport - DateTime.UtcNow).TotalHours);
                TempData["Message"] = $"You can export once every 24 hours. Try again in {hoursLeft} hour(s).";
                TempData["MessageType"] = "warning";
                return RedirectToAction("ManageLeads");
            }

            // Fetch all user leads
            var leads = await _context.UserLeads
    .Where(ul => ul.UserId == userId)
    .OrderByDescending(ul => ul.CreatedAt)
    .Select(ul => new
    {
        ul.Status,
        ul.Notes,
        ul.FollowUpDate,
        ul.CreatedAt,
        Name = ul.Lead.Name,
        Phone = ul.Lead.Phone,
        Email = ul.Lead.Email,
        OwnerName = ul.Lead.OwnerName,
        OwnerVerified = ul.Lead.OwnerVerified,
        EmailStatus = ul.Lead.EmailStatus,
        Website = ul.Lead.Website,
        Address = ul.Lead.Address,
        Niche = ul.Lead.Niche != null ? ul.Lead.Niche.Name : null,
        City = ul.Lead.City != null ? ul.Lead.City.Name : null,
        State = ul.Lead.State != null ? ul.Lead.State.Name : null,
        Country = ul.Lead.Country != null ? ul.Lead.Country.Name : null
    })
    .ToListAsync();

            // Build CSV
            var sb = new StringBuilder();

            // Header row
            sb.AppendLine("Name,Phone,Email,Owner Name,Owner Verified,Email Status,Website,Address,City,State,Country,Niche,Status,Notes,Follow Up Date,Date Added,Exported By,Export Date");
            // Data rows
            foreach (var ul in leads)
            {
                var status = ((LeadStatus)ul.Status).ToString().Replace("_", " ");

                sb.AppendLine(string.Join(",",
                    Escape(ul.Name),
                    Escape(ul.Phone),
                    Escape(ul.Email),
                    Escape(ul.OwnerName),
                    Escape(ul.OwnerVerified ? "Yes" : "No"),
                    Escape(string.IsNullOrWhiteSpace(ul.Email) ? "" : (ul.EmailStatus == 1 ? "Verified" : ul.EmailStatus == 2 ? "Catch-all" : "From website")),
                    Escape(ul.Website),
                    Escape(ul.Address),
                    Escape(ul.City),
                    Escape(ul.State),
                    Escape(ul.Country),
                    Escape(ul.Niche),
                    Escape(status),
                    Escape(ul.Notes),
                    ul.FollowUpDate.HasValue ? ul.FollowUpDate.Value.ToString("yyyy-MM-dd") : "",
                    ul.CreatedAt.ToString("yyyy-MM-dd"),
                    Escape(user.Email),
                    DateTime.UtcNow.ToString("yyyy-MM-dd")
                ));
            }

            // Update LastExportAt
            user.LastExportAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Return as file download
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"scoutix-leads-{DateTime.UtcNow:yyyy-MM-dd}.csv";
            return File(bytes, "text/csv", fileName);
        }

        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            // If value contains comma, newline or quote — wrap in quotes and escape inner quotes
            if (value.Contains(',') || value.Contains('\n') || value.Contains('"'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        // -----------------------------------------------------------------------
        // Enrichment endpoints
        // -----------------------------------------------------------------------

        [HttpPost]
        public async Task<IActionResult> EnrichLead(int leadId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            // Require an active subscription with enrichment access
            var subscription = await _context.Subscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.IsSubscriptionActive == true);
            var canEnrich = await _planService.HasFeatureAccessAsync(userId, p => p.CanEnrich);
            if (subscription == null || !canEnrich)
                return Json(new { queued = false, reason = "Enrichment requires an active subscription." });

            // Verify ownership
            var userLead = await _context.UserLeads
                .FirstOrDefaultAsync(ul => ul.LeadId == leadId && ul.UserId == userId);
            if (userLead == null)
                return NotFound();

            var lead = await _context.Leads.Include(l => l.Niche).FirstOrDefaultAsync(l => l.Id == leadId);
            if (lead == null)
                return NotFound();

            // No website AND not a registry-covered vertical = nothing to find, don't waste a job slot.
            // Dentist leads can still be resolved from name+phone via NPI / the dental board.
            var isDentist = EnrichmentVerticals.IsDentistry(lead.Niche?.Name);
            if (string.IsNullOrWhiteSpace(lead.Website) && !isDentist)
                return Json(new { queued = false, reason = "Lead has no website." });

            // Already in progress — don't queue a second job for the same lead
            if (lead.EnrichmentStatus == (int)Models.Enums.EnrichmentStatus.Pending)
                return Json(new { queued = false, reason = "Already in progress." });

            // Already completed — don't re-enrich (v1 decision, see ENRICHMENT_PLAN.md Part 5)
            if (lead.EnrichmentStatus == (int)Models.Enums.EnrichmentStatus.Completed)
                return Json(new { queued = false, reason = "Already enriched." });

            // Too many failures — stop retrying
            if (lead.EnrichmentAttempts >= 3)
                return Json(new { queued = false, reason = "Max attempts reached." });

            // Mark Pending synchronously so the status poller never sees None (0) while
            // the job waits in the queue, and a double-click can't enqueue a duplicate job.
            lead.EnrichmentStatus = (int)Models.Enums.EnrichmentStatus.Pending;
            await _context.SaveChangesAsync();

            _backgroundJobClient.Enqueue<EmailEnrichmentJob>("enrichment", job => job.RunAsync(leadId));

            return Json(new { queued = true });
        }

        [HttpPost]
        public async Task<IActionResult> EnrichLeads([FromBody] int[] leadIds)
        {
            if (leadIds == null || leadIds.Length == 0)
                return BadRequest();

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            // Require an active subscription with enrichment access
            var subscription = await _context.Subscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.IsSubscriptionActive == true);
            var canEnrich = await _planService.HasFeatureAccessAsync(userId, p => p.CanEnrich);
            if (subscription == null || !canEnrich)
                return Json(new { queued = 0, error = "Enrichment requires an active subscription." });

            // Only enqueue leads the user actually owns
            var ownedLeadIds = await _context.UserLeads
                .Where(ul => ul.UserId == userId && leadIds.Contains(ul.LeadId))
                .Select(ul => ul.LeadId)
                .ToListAsync();

            var leads = await _context.Leads
                .Include(l => l.Niche)
                .Where(l => ownedLeadIds.Contains(l.Id))
                .ToListAsync();

            var enqueuedIds = new List<int>();
            foreach (var lead in leads)
            {
                if (string.IsNullOrWhiteSpace(lead.Website) && !EnrichmentVerticals.IsDentistry(lead.Niche?.Name)) continue;
                if (lead.EnrichmentStatus == (int)Models.Enums.EnrichmentStatus.Pending) continue;   // already in progress
                if (lead.EnrichmentStatus == (int)Models.Enums.EnrichmentStatus.Completed) continue; // already done
                if (lead.EnrichmentAttempts >= 3) continue;                                           // max attempts hit

                // Mark Pending synchronously at enqueue time (not just inside the worker).
                // Closes the dedup window for the checks above so a double-click can't enqueue
                // duplicate jobs, and means the lead never reports None (0) to the status poller
                // while it sits in the queue waiting for a worker.
                lead.EnrichmentStatus = (int)Models.Enums.EnrichmentStatus.Pending;

                _backgroundJobClient.Enqueue<EmailEnrichmentJob>("enrichment", job => job.RunAsync(lead.Id));
                enqueuedIds.Add(lead.Id);
            }

            await _context.SaveChangesAsync();

            return Json(new { queued = enqueuedIds.Count, enqueuedIds });
        }

        [HttpGet]
        public async Task<IActionResult> EnrichmentStatus(string ids)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(ids))
                return BadRequest();

            var leadIds = ids.Split(',')
                .Select(s => int.TryParse(s.Trim(), out var id) ? id : 0)
                .Where(id => id > 0)
                .ToList();

            if (leadIds.Count == 0)
                return BadRequest();

            // Only return data for leads the user owns
            var ownedLeadIds = await _context.UserLeads
                .Where(ul => ul.UserId == userId && leadIds.Contains(ul.LeadId))
                .Select(ul => ul.LeadId)
                .ToListAsync();

            var statuses = await _context.Leads
                .Where(l => ownedLeadIds.Contains(l.Id))
                .Select(l => new
                {
                    l.Id,
                    l.EnrichmentStatus,
                    l.Email,
                    l.OwnerName,
                    l.OwnerVerified,
                    l.EmailStatus,
                })
                .ToListAsync();

            // Return as { "leadId": { status, email, ownerName, ownerVerified, emailStatus } }
            var result = statuses.ToDictionary(
                l => l.Id.ToString(),
                l => new
                {
                    status = l.EnrichmentStatus,
                    email = l.Email,
                    ownerName = l.OwnerName,
                    ownerVerified = l.OwnerVerified,
                    emailStatus = l.EmailStatus,
                }
            );

            return Json(result);
        }

    }
}