using Hangfire;
using Scoutix.Models;
using Scoutix.Models.Enums;
using Scoutix.Services.Enrichment;

namespace Scoutix.Jobs
{
    // Despite the name (kept so already-queued Hangfire jobs still resolve), this now runs the full
    // owner+email enrichment engine, not just the old website email scraper.
    public class EmailEnrichmentJob
    {
        private readonly ILeadOwnerEnrichmentService _enrichment;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<EmailEnrichmentJob> _logger;

        public EmailEnrichmentJob(
            ILeadOwnerEnrichmentService enrichment,
            ApplicationDbContext context,
            ILogger<EmailEnrichmentJob> logger)
        {
            _enrichment = enrichment;
            _context = context;
            _logger = logger;
        }

        [Queue("enrichment")]
        [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public async Task RunAsync(int leadId)
        {
            _logger.LogInformation("EmailEnrichmentJob: Starting for Lead {LeadId}.", leadId);

            // --- Mark Pending ---
            var lead = await _context.Leads.FindAsync(leadId);
            if (lead == null)
            {
                _logger.LogWarning("EmailEnrichmentJob: Lead {LeadId} not found. Skipping.", leadId);
                return;
            }

            // Only count a new attempt if this isn't a Hangfire retry of an already-pending job.
            if (lead.EnrichmentStatus != (int)EnrichmentStatus.Pending)
                lead.EnrichmentAttempts += 1;

            lead.EnrichmentStatus = (int)EnrichmentStatus.Pending;
            await _context.SaveChangesAsync();

            // --- Run the engine (owner + email) ---
            var outcome = await _enrichment.EnrichLeadAsync(leadId);

            // --- Write results back onto the lead ---
            var updated = await _context.Leads.FindAsync(leadId);
            if (updated == null) return;

            updated.EnrichmentStatus = (int)outcome.FinalStatus;
            updated.OwnerName = outcome.OwnerName;
            updated.OwnerVerified = outcome.OwnerVerified;
            updated.OwnerConfidence = outcome.OwnerConfidence;
            updated.OwnerSources = outcome.OwnerSources;
            updated.EmailStatus = outcome.EmailStatus;

            if (!string.IsNullOrWhiteSpace(outcome.Email))
                updated.Email = outcome.Email;

            if (outcome.FinalStatus == EnrichmentStatus.Completed)
                updated.EnrichedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "EmailEnrichmentJob: Lead {LeadId} finished {Status}. owner={Owner} email={Email}",
                leadId, outcome.FinalStatus, outcome.OwnerName ?? "none", outcome.Email ?? "none");
        }
    }
}
