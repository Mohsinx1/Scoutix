using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Scoutix.Filters;
using Scoutix.Jobs;
using Scoutix.Models;
using Scoutix.Enrichment.Configuration;
using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Email;
using Scoutix.Enrichment.Scraping;
using Scoutix.Enrichment.Sources.CoBoard;
using Scoutix.Enrichment.Sources.Npi;
using Scoutix.Enrichment.Sources.Website;
using Scoutix.Services.EmailService;
using Scoutix.Services.Enrichment;
using Scoutix.Services.GeoCache;
using Scoutix.Services.PlanService;
using Scoutix.Services.Scraping;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Application Insights — sends ILogger output to Azure Portal (Logs + Live Metrics).
// Connection string comes from APPLICATIONINSIGHTS_CONNECTION_STRING env var on Azure.
// AI 3.x throws at startup if the connection string is missing, so we only register
// the service when one is actually present (keeps local dev runs from crashing).
var aiConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"]
    ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
if (!string.IsNullOrWhiteSpace(aiConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

// Configure Sentry
builder.WebHost.UseSentry(o =>
{
    o.Dsn = builder.Configuration["Sentry:Dsn"];
    o.Debug = builder.Configuration.GetValue<bool>("Sentry:Debug");
    o.DiagnosticLevel = SentryLevel.Error;
    o.Environment = builder.Environment.EnvironmentName;
    o.Release = "scoutix@dev";
});

builder.Services.AddDataProtection()
    .SetApplicationName("scoutix")
    .PersistKeysToDbContext<ApplicationDbContext>();

builder.Services.AddSingleton<IGeoCache, GeoCache>();
builder.Services.AddScoped<IGoogleMapsScraper, GoogleMapsScraper>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IPlanService, PlanService>();
builder.Services.AddScoped<ExpiredSubscriptionJob>();
builder.Services.AddSingleton<IEmailExtractor, EmailExtractor>();
builder.Services.AddScoped<IEmailEnrichmentService, EmailEnrichmentService>();
builder.Services.AddScoped<EmailEnrichmentJob>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Owner-enrichment engine persists candidates/provenance into the same DB (separate tables).
builder.Services.AddDbContext<EnrichmentDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Owner-enrichment engine wiring (Milestone 2) ──
var enrichmentCfg = new EnrichmentConfig();
builder.Configuration.GetSection("Enrichment").Bind(enrichmentCfg);
enrichmentCfg.Email.SmtpProbe = false; // outbound port 25 is blocked — permutations stay unverified until a verifier/port-25 is wired

builder.Services.AddHttpClient("enrichment", c =>
{
    c.Timeout = TimeSpan.FromSeconds(20);
    c.DefaultRequestHeaders.UserAgent.ParseAdd(enrichmentCfg.UserAgent);
});

builder.Services.AddSingleton<IBrowserProvider>(_ => new PlaywrightBrowserProvider(enrichmentCfg.UserAgent, headless: true));
builder.Services.AddSingleton<IEmailVerifier>(sp => new SmtpEmailVerifier(enrichmentCfg.Email, sp.GetRequiredService<ILogger<SmtpEmailVerifier>>()));

builder.Services.AddScoped<IPageCache, EfPageCache>();
builder.Services.AddScoped<ICandidateStore, CandidateStore>();
builder.Services.AddScoped<IListingStore, ListingStore>();
builder.Services.AddScoped<IEmailStage, EmailStage>();

builder.Services.AddScoped(sp => new WebsiteOwnerSource(
    sp.GetRequiredService<IBrowserProvider>(), sp.GetRequiredService<IPageCache>(),
    enrichmentCfg.RateLimit, sp.GetRequiredService<ILogger<WebsiteOwnerSource>>()));
builder.Services.AddScoped(sp => new NpiRegistrySource(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("enrichment"),
    sp.GetRequiredService<IPageCache>(), sp.GetRequiredService<ILogger<NpiRegistrySource>>()));
builder.Services.AddScoped(sp => new ColoradoDentalBoardSource(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("enrichment"),
    sp.GetRequiredService<IPageCache>(), sp.GetRequiredService<ILogger<ColoradoDentalBoardSource>>(),
    enrichmentCfg.ColoradoAppToken));

builder.Services.AddScoped<ILeadEnricherFactory, LeadEnricherFactory>();
builder.Services.AddScoped<ILeadOwnerEnrichmentService, LeadOwnerEnrichmentService>();

builder.Services.AddHangfire(x => x.UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection")));

// Main server handles scraping/export queues — default worker count (ProcessorCount × 5)
builder.Services.AddHangfireServer(options =>
{
    options.ServerName = "main-server";
    options.Queues = new[] { "pro", "growth", "starter" };
});

// Dedicated enrichment server — strictly 2 concurrent Playwright browsers to protect server memory
builder.Services.AddHangfireServer(options =>
{
    options.ServerName = "enrichment-server";
    options.Queues = new[] { "enrichment" };
    options.WorkerCount = 2;
});
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
    });

builder.Services.AddHttpClient();
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AuthorizeFilter());
});

var app = builder.Build();

// Create DataProtectionKeys table if it doesn't exist
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.ExecuteSqlRaw("""
        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DataProtectionKeys' AND xtype='U')
        CREATE TABLE [DataProtectionKeys] (
            [Id] int NOT NULL IDENTITY,
            [FriendlyName] nvarchar(max) NULL,
            [Xml] nvarchar(max) NULL,
            CONSTRAINT [PK_DataProtectionKeys] PRIMARY KEY ([Id])
        )
        """);
}

// Load geoCache
using (var scope = app.Services.CreateScope())
{
    var geoCache = scope.ServiceProvider.GetRequiredService<IGeoCache>();
    geoCache.Load();
}
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// CSP Middleware
app.Use(async (context, next) =>
{
    var cspHeader = "frame-ancestors 'self' https://localhost https://buy.paddle.com https://scoutix.io; " +
                "img-src * data: blob:;";
    context.Response.Headers.Add("Content-Security-Policy", cspHeader);
    await next();
});

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    if (context.Request.Host.Host.StartsWith("www."))
    {
        var newHost = context.Request.Host.Host.Substring(4);
        var newUrl = $"{context.Request.Scheme}://{newHost}{context.Request.Path}{context.Request.QueryString}";
        context.Response.Redirect(newUrl, permanent: true);
        return;
    }
    await next();
});
app.UseRouting();
app.UseStaticFiles();
app.UseAuthentication();

// Add user details to Sentry scope if authenticated
app.Use(async (context, next) =>
{
    if (context.User?.Identity?.IsAuthenticated == true)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = context.User.FindFirstValue(ClaimTypes.Email);
        var name = context.User.FindFirstValue(ClaimTypes.Name);
        SentrySdk.ConfigureScope(scope =>
        {
            scope.User = new Sentry.SentryUser
            {
                Id = userId,
                Email = email,
                Username = name
            };
            scope.SetTag("app.user_id", userId ?? "");
            scope.SetTag("app.user_email", email ?? "");
        });
    }
    await next();
});
// Prevent caching of authenticated pages
app.Use(async (context, next) =>
{
    if (context.User?.Identity?.IsAuthenticated == true)
    {
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
    }
    await next();
});

app.UseSession();
app.UseAuthorization();
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthFilter(app.Configuration) }
});
// Schedule recurring jobs
RecurringJob.AddOrUpdate<ExpiredSubscriptionJob>(
    "revoke-expired-subscriptions",
    job => job.RevokeExpiredSubscriptions(),
    Cron.Daily
);

app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();