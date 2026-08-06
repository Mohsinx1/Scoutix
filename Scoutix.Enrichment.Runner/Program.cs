using System.Net.Sockets;
using System.Text;
using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Scoutix.Enrichment;
using Scoutix.Enrichment.Configuration;
using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Email;
using Scoutix.Enrichment.Reporting;
using Scoutix.Enrichment.Scraping;
using Scoutix.Enrichment.Sources;
using Scoutix.Enrichment.Sources.CoBoard;
using Scoutix.Enrichment.Sources.Listings;
using Scoutix.Enrichment.Sources.Npi;
using Scoutix.Enrichment.Sources.Stubs;
using Scoutix.Enrichment.Sources.Website;

if (args.Length == 0)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  run --input <csv> [--vertical dentists] [--city \"Denver, CO\"] [--count 100] [--concurrency 3]");
    Console.WriteLine("  --probe-extract \"<text>\"            name/role matches for a string");
    Console.WriteLine("  --probe-website <url>                WebsiteOwnerSource against a site");
    Console.WriteLine("  --probe-npi <name> <city> [prior] [phone]");
    Console.WriteLine("  --probe-board <name> <city> [prior]");
    return;
}

switch (args[0])
{
    case "run": await RunBatchAsync(args); break;
    case "report": await RunReportAsync(BuildConfig(ParseOptions(args))); break;
    case "stats": await StatsAsync(); break;
    case "reset": await ResetAsync(); break;
    case "convert": ConvertXlsxToCsv(args[1], args[2]); break;
    case "schema-script": Console.Write(EnrichmentDbContextFactory.Create(LoadConfig().ConnectionString).Database.GenerateCreateScript()); break;
    case "--probe-extract": ProbeExtract(string.Join(' ', args.Skip(1))); break;
    case "--probe-website": await ProbeWebsiteAsync(args[1]); break;
    case "--probe-npi": await ProbeNpiAsync(args[1], args[2], args.ElementAtOrDefault(3), args.ElementAtOrDefault(4)); break;
    case "--probe-board": await ProbeBoardAsync(args[1], args[2], args.ElementAtOrDefault(3)); break;
    case "--probe-email": await ProbeEmailAsync(args[1]); break;
    default: Console.WriteLine($"Unknown command: {args[0]}"); break;
}

// --------------------------------------------------------------------------------------
// Batch run
// --------------------------------------------------------------------------------------

static async Task RunBatchAsync(string[] args)
{
    var opts = ParseOptions(args);
    var cfg = BuildConfig(opts);

    if (!opts.TryGetValue("input", out var inputPath))
    {
        Console.WriteLine("run requires --input <csv>");
        return;
    }

    Console.WriteLine($"Enrichment run: vertical={cfg.Vertical}  city=\"{cfg.City}\"  count={cfg.TargetCount}  concurrency={cfg.MaxConcurrency}");
    Console.WriteLine($"Input: {inputPath}");

    // 1. Make sure the isolated enrichment DB + tables exist.
    await using (await EnrichmentDbContextFactory.CreateAndEnsureCreatedAsync(cfg.ConnectionString)) { }

    // 2. Import listings (idempotent upsert on SourceKey).
    var raw = await new CsvListingSource(inputPath, cfg.Vertical, cfg.City, cfg.TargetCount).ReadAsync();
    var listings = new List<Listing>();
    await using (var db = EnrichmentDbContextFactory.Create(cfg.ConnectionString))
    {
        var store = new ListingStore(db);
        foreach (var l in raw) listings.Add(await store.UpsertListingAsync(l));
    }
    Console.WriteLine($"Imported {listings.Count} listings.\n");

    // 3. Shared resources across the batch.
    await using var browser = new PlaywrightBrowserProvider(cfg.UserAgent, headless: true);
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd(cfg.UserAgent);
    using var lf = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));

    if (cfg.Email.SmtpProbe && !await SmtpReachableAsync(cfg.Email.SmtpTimeoutMs))
    {
        Console.WriteLine("Note: outbound SMTP (port 25) is blocked here — disabling SMTP probing.");
        Console.WriteLine("      Emails are still captured + MX-validated (status Unknown); plug an external verifier in for true deliverability.\n");
        cfg.Email.SmtpProbe = false;
    }
    var verifier = new SmtpEmailVerifier(cfg.Email, lf.CreateLogger<SmtpEmailVerifier>());

    // 4. Enrich with bounded concurrency; each listing gets its own DbContext.
    var sem = new SemaphoreSlim(Math.Max(1, cfg.MaxConcurrency));
    var tasks = listings.Select(async listing =>
    {
        await sem.WaitAsync();
        try
        {
            await using var db = EnrichmentDbContextFactory.Create(cfg.ConnectionString);
            var cache = new EfPageCache(db);
            var candidateStore = new CandidateStore(db);
            var sources = BuildSources(cfg, browser, http, cache, lf);
            IEmailStage? emailStage = cfg.Sources.EmailVerification
                ? new EmailStage(verifier, cache, new EmailExtractor(), candidateStore, lf.CreateLogger<EmailStage>())
                : null;
            var enricher = new ListingEnricher(sources, candidateStore, new ListingStore(db),
                lf.CreateLogger<ListingEnricher>(), emailStage);
            await enricher.EnrichAsync(listing);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ! listing {listing.Id} ({listing.Name}) failed: {ex.Message}");
        }
        finally { sem.Release(); }
    });
    await Task.WhenAll(tasks);

    // 5. Coverage report.
    Console.WriteLine();
    await RunReportAsync(cfg);
}

static EnrichmentConfig BuildConfig(Dictionary<string, string> opts)
{
    var cfg = LoadConfig();
    if (opts.TryGetValue("vertical", out var v)) cfg.Vertical = v;
    if (opts.TryGetValue("city", out var c)) cfg.City = c;
    if (opts.TryGetValue("count", out var n) && int.TryParse(n, out var count)) cfg.TargetCount = count;
    if (opts.TryGetValue("concurrency", out var cc) && int.TryParse(cc, out var conc)) cfg.MaxConcurrency = conc;
    if (opts.TryGetValue("connection", out var conn)) cfg.ConnectionString = conn;
    return cfg;
}

static async Task RunReportAsync(EnrichmentConfig cfg)
{
    await using var db = EnrichmentDbContextFactory.Create(cfg.ConnectionString);

    var listings = await db.Listings
        .Where(l => l.Vertical == cfg.Vertical && l.City == cfg.City)
        .OrderBy(l => l.Id)
        .ToListAsync();
    var ids = listings.Select(l => l.Id).ToList();

    var candidates = await db.FieldCandidates.Where(c => ids.Contains(c.ListingId)).ToListAsync();
    var rollups = await db.ListingEnrichments.Where(e => ids.Contains(e.ListingId)).ToListAsync();

    var candByListing = candidates.GroupBy(c => c.ListingId)
        .ToDictionary(g => g.Key, g => (IReadOnlyList<FieldCandidate>)g.ToList());
    var rollupByListing = rollups.ToDictionary(e => e.ListingId);

    var rows = listings
        .Select(l => new ListingReportRow(
            l,
            rollupByListing.GetValueOrDefault(l.Id),
            candByListing.GetValueOrDefault(l.Id, Array.Empty<FieldCandidate>())))
        .ToList();

    var classified = CoverageReport.Classify(rows);
    var metrics = CoverageReport.Aggregate(classified, cfg.Vertical, cfg.City);
    Console.WriteLine(CoverageReport.RenderConsole(metrics));

    var dir = "reports";
    Directory.CreateDirectory(dir);
    var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var summaryPath = Path.Combine(dir, $"coverage_{stamp}.csv");
    var candPath = Path.Combine(dir, $"candidates_{stamp}.csv");
    CoverageReport.WriteSummaryCsv(classified, summaryPath);
    CoverageReport.WriteCandidatesCsv(rows, candPath);
    Console.WriteLine($"CSV written:\n  {Path.GetFullPath(summaryPath)}\n  {Path.GetFullPath(candPath)}");
}

static async Task ResetAsync()
{
    var cfg = LoadConfig();
    await using var db = EnrichmentDbContextFactory.Create(cfg.ConnectionString);
    await db.Database.EnsureDeletedAsync();
    await db.Database.EnsureCreatedAsync();
    Console.WriteLine("Enrichment DB reset (dropped + recreated).");
}

static async Task StatsAsync()
{
    var cfg = LoadConfig();
    await using var db = EnrichmentDbContextFactory.Create(cfg.ConnectionString);

    var listings = await db.Listings.CountAsync();
    var candidates = await db.FieldCandidates.CountAsync();
    Console.WriteLine($"listings={listings}  candidates={candidates}");

    var rollups = await db.ListingEnrichments.OrderBy(e => e.ListingId).ToListAsync();
    foreach (var r in rollups)
        Console.WriteLine($"  L{r.ListingId}: owner={r.OwnerName ?? "-"}  verified={r.OwnerNameVerified}  " +
                          $"sources=[{r.OwnerNameSources}]  |  email={r.Email ?? "-"} ({r.EmailStatus})");
}

static List<IOwnerNameSource> BuildSources(
    EnrichmentConfig cfg, IBrowserProvider browser, HttpClient http, IPageCache cache, ILoggerFactory lf)
{
    var sources = new List<IOwnerNameSource>();
    if (cfg.Sources.Website)
        sources.Add(new WebsiteOwnerSource(browser, cache, cfg.RateLimit, lf.CreateLogger<WebsiteOwnerSource>()));
    if (cfg.Sources.Npi)
        sources.Add(new NpiRegistrySource(http, cache, lf.CreateLogger<NpiRegistrySource>()));
    if (cfg.Sources.StateLicenseBoard)
        sources.Add(new ColoradoDentalBoardSource(http, cache, lf.CreateLogger<ColoradoDentalBoardSource>(), cfg.ColoradoAppToken));
    if (cfg.Sources.SearchEngine)
        sources.Add(new SearchEngineSource());
    if (cfg.Sources.SecretaryOfState)
        sources.Add(new SecretaryOfStateSource());
    return sources;
}

static EnrichmentConfig LoadConfig()
{
    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .Build();
    var cfg = new EnrichmentConfig();
    config.GetSection("Enrichment").Bind(cfg);
    return cfg;
}

static void ConvertXlsxToCsv(string xlsxPath, string csvPath)
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    using var stream = File.Open(xlsxPath, FileMode.Open, FileAccess.Read);
    using var reader = ExcelReaderFactory.CreateReader(stream);

    var sb = new StringBuilder();
    int rows = 0, cols = 0;
    while (reader.Read())
    {
        var cells = new List<string>();
        for (int i = 0; i < reader.FieldCount; i++)
            cells.Add(CsvEscape(reader.GetValue(i)?.ToString() ?? ""));
        sb.AppendLine(string.Join(",", cells));
        cols = Math.Max(cols, reader.FieldCount);
        rows++;
    }
    File.WriteAllText(csvPath, sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"Converted -> {csvPath}  (rows={rows} incl header, cols={cols})");
}

static string CsvEscape(string v) =>
    v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r')
        ? "\"" + v.Replace("\"", "\"\"") + "\""
        : v;

static async Task<bool> SmtpReachableAsync(int timeoutMs)
{
    try
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("aspmx.l.google.com", 25).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
        return true;
    }
    catch { return false; }
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 1; i < args.Length - 1; i++)
        if (args[i].StartsWith("--"))
            opts[args[i][2..]] = args[i + 1];
    return opts;
}

// --------------------------------------------------------------------------------------
// Probes (debugging single sources)
// --------------------------------------------------------------------------------------

static void ProbeExtract(string text)
{
    Console.WriteLine($"Input: {text}");
    foreach (var m in Scoutix.Enrichment.Sources.Website.RoleNameExtractor.Extract(text))
        Console.WriteLine($"   - {m.Name}  [{m.Role}]  conf={m.Confidence:0.00}");
}

static async Task ProbeWebsiteAsync(string website)
{
    await using var browser = new PlaywrightBrowserProvider("ScoutixEnrichmentBot/1.0 (+https://scoutix.io/bot)", headless: true);
    var source = new WebsiteOwnerSource(browser, new NullPageCache(),
        new RateLimitOptions { MinDelayMs = 300, MaxDelayMs = 800 }, NullLogger<WebsiteOwnerSource>.Instance);

    var candidates = await source.FindOwnersAsync(
        new OwnerLookupContext { Listing = new Listing { Name = "probe", Website = website } });

    Console.WriteLine($"Website: {website}  -> {candidates.Count} candidate(s):");
    foreach (var c in candidates) Console.WriteLine($"   - {c.Name}  conf={c.Confidence:0.00}  ({c.Detail})");
}

static async Task ProbeNpiAsync(string businessName, string city, string? priorName, string? phone)
{
    if (phone == "-") phone = null;
    if (priorName == "-") priorName = null;
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("ScoutixEnrichmentBot/1.0 (+https://scoutix.io/bot)");
    using var lf = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Debug));
    var source = new NpiRegistrySource(http, new NullPageCache(), lf.CreateLogger<NpiRegistrySource>());

    var prior = string.IsNullOrWhiteSpace(priorName) ? Array.Empty<OwnerNameCandidate>() : new[] { new OwnerNameCandidate(priorName, 0.45, "website") };
    var candidates = await source.FindOwnersAsync(new OwnerLookupContext
    {
        Listing = new Listing { Name = businessName, City = city, Phone = phone },
        PriorCandidates = prior,
    });

    Console.WriteLine($"NPI: '{businessName}'  city='{city}'  prior='{priorName ?? "-"}'  phone='{phone ?? "-"}'  -> {candidates.Count}");
    foreach (var c in candidates) Console.WriteLine($"   - {c.Name}  conf={c.Confidence:0.00}  ({c.Detail})");
}

static async Task ProbeBoardAsync(string businessName, string city, string? priorName)
{
    if (priorName == "-") priorName = null;
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("ScoutixEnrichmentBot/1.0 (+https://scoutix.io/bot)");
    using var lf = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Debug));
    var source = new ColoradoDentalBoardSource(http, new NullPageCache(), lf.CreateLogger<ColoradoDentalBoardSource>());

    var prior = string.IsNullOrWhiteSpace(priorName) ? Array.Empty<OwnerNameCandidate>() : new[] { new OwnerNameCandidate(priorName, 0.45, "website") };
    var candidates = await source.FindOwnersAsync(new OwnerLookupContext
    {
        Listing = new Listing { Name = businessName, City = city },
        PriorCandidates = prior,
    });

    Console.WriteLine($"CO board: '{businessName}'  city='{city}'  prior='{priorName ?? "-"}'  -> {candidates.Count}");
    foreach (var c in candidates) Console.WriteLine($"   - {c.Name}  conf={c.Confidence:0.00}  ({c.Detail})");
}

static async Task ProbeEmailAsync(string email)
{
    using var lf = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Debug));
    var verifier = new SmtpEmailVerifier(new EmailVerifyOptions(), lf.CreateLogger<SmtpEmailVerifier>());
    var r = await verifier.VerifyAsync(email);
    Console.WriteLine($"{r.Email}: status={r.Status}  conf={r.Confidence:0.00}  role={r.IsRole}  catchAll={r.IsCatchAll}  ({r.Detail})");
}
