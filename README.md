# Scoutix

A B2B lead-generation and CRM product for small-business sales teams. It scraped Google Maps for local business prospects, deduplicated and stored them in a built-in CRM, and enriched them with owner names and contact emails.

**This product is no longer operating.** It ran in production on a single VPS for a few months in 2026 and was shut down in June 2026. This repository is published as a code sample. It is not maintained, has no CI, and the credentials it once used have been rotated and removed.

---

## What it did

A user picked a business vertical (dentists, roofers, HVAC) and a geography (city, state, or country, depending on their plan). The app queued a background scraping job, drove a headless Chromium session over Google Maps, and wrote deduplicated leads — name, phone, website, address — into the user's CRM. From there they could filter, track lead status through a pipeline, set follow-up reminders, and export to CSV.

The last feature built was **owner enrichment**: given a business listing, resolve the actual owner's name and a working email address by scraping the business website, cross-referencing public registries (NPI for medical, state license boards), and generating/scoring email permutations. It shipped after the product's commercial window had effectively closed.

## Stack

| Layer | Choice |
|---|---|
| Runtime | ASP.NET Core (.NET 10), Razor MVC |
| Data | EF Core over SQL Server (Azure SQL in production) |
| Background jobs | Hangfire, SQL Server storage, four named queues |
| Scraping | Playwright + headless Chromium |
| Payments | Paddle (checkout + webhooks) |
| Email | Mailgun |
| Errors | Sentry |
| Deploy | Docker container on a Linux VPS behind nginx, Let's Encrypt TLS |

Schema was managed with hand-written idempotent SQL (`db/owner_enrichment.sql`) rather than EF migrations — a deliberate trade-off discussed below under *Known weaknesses*.

## Layout

```
Scoutix/                      ASP.NET Core web app — controllers, views, jobs, entitlements
Scoutix.Enrichment/           Owner + email enrichment engine (no web dependency)
Scoutix.Enrichment.Runner/    CLI harness for running the engine against a CSV, offline
Scoutix.Tests/                xUnit tests over the enrichment engine's pure logic
db/                           Idempotent schema scripts
```

The enrichment engine is deliberately a separate assembly with no ASP.NET dependency, so it could be exercised from the CLI runner against a CSV of listings without standing up the web app or touching the production database. That is what made the pure-logic parts (name normalization, email classification, permutation scoring, NPI matching) testable at all.

## Running it

```bash
cp Scoutix/appsettings.Example.json Scoutix/appsettings.json
```

Fill in the values — `appsettings.Example.json` documents every key and which ones are secret. Then point `ConnectionStrings:DefaultConnection` at a SQL Server instance, run `db/owner_enrichment.sql`, and `dotnet run --project Scoutix`. Hangfire creates its own schema on first start.

`appsettings.json` and `appsettings.*.json` are gitignored; only the `.Example.json` reference is tracked.

---

# Engineering deep-dive

Two problems in here were more interesting than the rest. Both are about *correctness under contention* — one for shared compute, one for money.

## 1. Tiered background work on a single box

### The constraint

Lead generation is slow, memory-hungry, and unbounded: a Pro user could request 500 leads across a whole country, which means driving Chromium through hundreds of Google Maps result pages. It obviously has to be a background job. The hard part was that everything ran on **one 4-vCPU / 8 GB VPS**, shared by every customer on every tier. There was no horizontal scale to hide behind. If a Starter user kicked off a big scrape, a Pro user paying 4× more would sit behind them in a FIFO queue and feel like the product was broken.

So paid tiers had to actually *be* faster under contention, using scheduling alone.

### Decision 1 — strict priority via queue declaration order

Hangfire resolves queues in the order they are declared on the server. A worker looking for work drains the first queue completely before it looks at the second.

```csharp
// Program.cs
builder.Services.AddHangfireServer(options =>
{
    options.ServerName = "main-server";
    options.Queues = new[] { "pro", "growth", "starter" };
});
```

That single array *is* the priority mechanism. Each plan carries its queue name as part of its entitlement config, so the routing decision is data, not a branch:

```csharp
// Models/Entitlements/PlanFeatures.cs
[3] = new PlanConfig { PlanName = "Pro", HangfireQueue = "pro",     ScrapingDelayMs = 0,    ... }
[2] = new PlanConfig { PlanName = "Growth", HangfireQueue = "growth", ScrapingDelayMs = 1500, ... }
[1] = new PlanConfig { PlanName = "Starter", HangfireQueue = "starter", ScrapingDelayMs = 4000, ... }
```

```csharp
// LeadsController.cs
var jobId = BackgroundJob.Enqueue(planConfig.HangfireQueue,
    () => GenerateLeadsBackground(nicheId, leadsToGenerate, countryId, stateId, cityId, userId));
```

Note the second, independent lever: `ScrapingDelayMs`. Queue priority governs *when your job starts*; the inter-request delay governs *how fast it runs once started* (0 ms for Pro, 4000 ms for Starter). Separating "time to first result" from "throughput once running" mattered because they fail differently — a Pro user with an empty queue still wants their scrape to finish fast, and a Starter user who is alone on the box should still be rate-limited so the scraper doesn't look like a bot to Google.

**Why strict priority and not weighted fair queuing:** strict priority is the one scheduling discipline Hangfire gives you for free, with zero moving parts and no custom `IBackgroundJobFactory`. Weighted or aging schemes need state and tuning. At the actual scale — single-digit concurrent customers, queue depth usually zero — the sophisticated version would have been unfalsifiable complexity solving a problem nobody had yet.

**The honest cost:** strict priority *starves*. A sustained Pro backlog blocks Starter jobs indefinitely, with no aging to rescue them. This design is only defensible because saturation was implausible at that customer count. At real volume it needs job aging (promote a job's effective priority as it waits) or weighted round-robin. I knew this was a scale-limited choice when I made it; it was the right call for the scale I had, and it is the first thing that would have broken.

### Decision 2 — a second Hangfire server, because the failure modes differ

The enrichment work is not just "more background jobs." It is a *different kind* of load. Scraping is IO-bound and mostly waiting on network. Enrichment drives a full Playwright browser per job, and Chromium peaked around 5.6 GB on an 8 GB box.

Hangfire's default worker count is `ProcessorCount × 5` — on 4 vCPUs, **20 concurrent workers**. Twenty simultaneous Chromium instances would OOM the machine instantly. So enrichment got its own server with a hard cap:

```csharp
builder.Services.AddHangfireServer(options =>
{
    options.ServerName = "enrichment-server";
    options.Queues = new[] { "enrichment" };
    options.WorkerCount = 2;      // memory ceiling, not a throughput target
});
```

This is the part I think is worth the most: **queue priority and worker isolation solve two different problems and are easy to conflate.** Priority is about *fairness between customers*. Worker-count isolation is about *resource safety* — making sure one class of work can't exhaust the box out from under another. Putting enrichment on the `starter` queue with a low priority would have been the naive fix, and it would still have let 20 browsers spawn the moment the scrape queues went quiet. Only a separate server with its own worker ceiling actually bounds the memory.

`WorkerCount = 2` is a memory budget expressed as concurrency. That framing — pick the number from the resource ceiling, not from desired throughput — is what made it correct.

## 2. Paddle webhook validation: authenticity and idempotency are different problems

Payment webhooks are the highest-consequence untrusted input in the system. A forged `subscription.activated` grants a paid plan for free; a double-processed one corrupts subscription state. `WebhookController.cs` handles these as two separate concerns, in order, because conflating them is the common bug.

### Step 1 — verify authenticity against the *raw* body

```csharp
Request.EnableBuffering();
string notificationJson;
using (var reader = new StreamReader(Request.Body, leaveOpen: true))
{
    notificationJson = await reader.ReadToEndAsync();
}
```

The critical detail: **the HMAC must be computed over the exact bytes Paddle signed.** The obvious implementation — bind to a typed model, then re-serialize to check the signature — silently fails, because JSON serializers do not round-trip byte-for-byte. Key order, whitespace, and number formatting all shift, and the hash no longer matches. So the raw body is read as a string *before* any deserialization, and `EnableBuffering()` rewinds the stream so the rest of the pipeline can still read it.

Paddle sends `Paddle-Signature: ts=<unix>;h1=<hex>`. The signed payload is the timestamp and the raw body joined by a colon:

```csharp
var signedPayload = $"{ts}:{notificationJson}";
using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
var computedHex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload))).ToLower();

if (!CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(computedHex),
        Encoding.UTF8.GetBytes(h1)))
{
    return Unauthorized();
}
```

`FixedTimeEquals`, not `==`. Ordinary string comparison short-circuits at the first differing byte, so response latency leaks how many leading characters an attacker guessed correctly — enough to recover a valid signature byte by byte across many requests. Constant-time comparison closes that oracle. This is a small line with a large consequence, and it is the sort of thing that is invisible in code review unless you are looking for it.

### Step 2 — idempotency, which the signature does *not* give you

A valid signature proves Paddle sent this event. It proves nothing about whether you already processed it. Paddle retries on any non-2xx response, so delivery is **at-least-once**: a handler that upgrades a subscription and then throws will be retried, and a naive implementation applies the upgrade twice.

So every event is recorded in a `WebhookLogs` ledger keyed on Paddle's `event_id`, with three distinct outcomes:

```csharp
var existingLog = await _context.WebhookLogs
    .FirstOrDefaultAsync(w => w.PaddleEventId == eventId);

if (existingLog != null)
{
    if (existingLog.ProcessedSuccessfully == true)
        return Ok();                       // already done — absorb the retry

    webhookLog = existingLog;              // previously failed — reuse the row and retry
    webhookLog.ErrorMessage = null;
    webhookLog.ReceivedAt = DateTime.UtcNow;
}
```

Distinguishing *already succeeded* from *previously failed* is what makes retries safe in both directions: a duplicate of a successful event is swallowed, while a genuine retry of a failed one is allowed to proceed rather than being mistaken for a duplicate and dropped.

### Step 3 — the database is the real serialization point

The check-then-insert above is **not atomic**. Two concurrent deliveries of the same event can both read "no existing log" and both proceed. The pre-check is an optimization; the actual guarantee comes from a unique index, and the race is caught explicitly:

```csharp
catch (DbUpdateException ex)
    when (ex.InnerException?.Message.Contains("UX_WebhookLogs_PaddleEventId") == true)
{
    _logger.LogInformation("Race condition duplicate webhook ignored: {EventId}", eventId);
    return Ok();
}
```

Catching the *named* constraint rather than any `DbUpdateException` matters — a blanket catch would swallow unrelated write failures and return `200 OK` to Paddle, permanently losing an event that should have been retried.

### Step 4 — status codes are control flow

Because Paddle retries on non-2xx, every response code is a deliberate instruction to the sender:

- **`200`** on duplicate or already-processed — *stop retrying, this is settled.*
- **`401`** on missing or bad signature — *reject, reveal nothing about why.*
- **`500`** on an unhandled handler exception, after persisting `ErrorMessage` — *please retry; we recorded why we failed.*

Returning `200` on a handler failure would silently drop real payment events. Returning `500` on a duplicate would cause Paddle to retry forever against a request that will never succeed differently. The response code is not cosmetic; it is the retry protocol.

### Known weakness in this design

The `ts` value is included in the signed payload but **never checked for freshness**. A captured valid request could be replayed indefinitely. The idempotency ledger blunts this — replaying an already-processed `event_id` is a no-op — but that is defence in depth by accident, not by design. The correct fix is rejecting any request whose `ts` is more than a few minutes old, which is roughly five lines and should have been there. I am noting it rather than quietly leaving it in the code for someone else to find.

---

## Known weaknesses

Stated plainly, since this is a shutdown archive rather than a maintained project:

- **No CI and no automated deploys.** Deploys were a manual `docker build` / `push` / `docker run` sequence over SSH. Fine for one operator; not defensible with a team.
- **Test coverage is narrow.** The enrichment engine's pure logic has real xUnit coverage — name normalization, email classification and permutation scoring, NPI matching, coverage reporting. Controllers, the webhook pipeline, and anything touching EF Core have none, which is exactly backwards from where the risk actually sits.
- **No EF migrations.** Schema was hand-written idempotent SQL applied manually. This kept early iteration fast and made the production Azure SQL changes explicit and reviewable, but it means schema and model can drift with nothing to catch it.
- **`dynamic` + Newtonsoft for webhook payloads.** Loosely typed parsing of the most security-sensitive input in the system. Typed DTOs with explicit validation would be strictly better.
- **Strict-priority queue starvation**, as described above.
- **No webhook replay window**, as described above.
- Secrets were environment-file based, not a managed secret store.
