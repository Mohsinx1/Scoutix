using Scoutix.Models;
using Scoutix.Services.GeoCache;
using Microsoft.AspNetCore.Mvc;

namespace Scoutix.Controllers
{
    [Route("api/geo")]
    [ApiController]
    public class GeoController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IGeoCache _geoCache;

        public GeoController(ApplicationDbContext context, IGeoCache geoCache)
        {
            _context = context;
            _geoCache = geoCache;
        }

        [HttpGet("niches")]
        public IActionResult GetNiches()
        {
            var result = _context.Niches.OrderBy(c => c.Name).Select(c => new { c.Id, c.Name }).ToList();
            return Ok(result);
        }

        [HttpGet("countries")]
        public IActionResult GetCountries()
        {
            var result = _geoCache.GetCountries()
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name })
                .ToList();
            return Ok(result);
        }

        [HttpGet("states")]
        public IActionResult GetStates(int countryId, string? search = null)
        {
            var result = _geoCache.GetStatesByCountry(countryId)
                .Where(s => string.IsNullOrWhiteSpace(search) || s.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Name)
                .Take(50)
                .Select(s => new { s.Id, s.Name })
                .ToList();
            return Ok(result);
        }

        [HttpGet("cities")]
        public IActionResult GetCities(int stateId, string? search = null)
        {
            var result = _geoCache.GetCitiesByState(stateId)
                .Where(c => string.IsNullOrWhiteSpace(search) || c.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Name)
                .Take(50)
                .Select(c => new { c.Id, c.Name })
                .ToList();
            return Ok(result);
        }
    }
}