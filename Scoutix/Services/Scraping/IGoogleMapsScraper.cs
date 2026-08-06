using Scoutix.Models;
using Scoutix.Models.Enums;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Scoutix.Services.Scraping
{
    public interface IGoogleMapsScraper
    {
        Task<Dictionary<string, bool>> ScrapeAndSaveLeadsAsync(
      int nicheId,
      string niche,
      IEnumerable<LeadGenQuery> queries,
      int requiredLeads,
      HashSet<string> existingPhones,
      int userId,
      CancellationToken cancellationToken = default);
    }
}