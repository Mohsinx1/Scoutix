using Scoutix.Models;

namespace Scoutix.Services.GeoCache
{
    public class GeoCache : IGeoCache
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private List<Country> _countries = new();
        private Dictionary<int, List<State>> _statesByCountry = new();
        private readonly Dictionary<int, List<City>> _citiesByState = new();
        private readonly object _citiesLock = new();

        public GeoCache(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public void Load()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Only load countries and states at startup — small tables, fast
            _countries = context.Countries.ToList();
            var states = context.States.ToList();

            _statesByCountry = states
                .GroupBy(s => s.CountryId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Cities are NOT loaded here — table is too large.
            // They are loaded on demand per state and cached below.
        }

        public IReadOnlyList<Country> GetCountries()
        {
            return _countries;
        }

        public IReadOnlyList<State> GetStatesByCountry(int countryId)
        {
            return _statesByCountry.TryGetValue(countryId, out var states)
                ? states
                : new List<State>();
        }

        public IReadOnlyList<City> GetCitiesByState(int stateId)
        {
            lock (_citiesLock)
            {
                if (_citiesByState.TryGetValue(stateId, out var cached))
                    return cached;

                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var cities = context.Cities
                    .Where(c => c.StateId == stateId)
                    .ToList();

                _citiesByState[stateId] = cities;
                return cities;
            }
        }
    }
}