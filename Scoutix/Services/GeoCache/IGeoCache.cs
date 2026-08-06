using Scoutix.Models;

namespace Scoutix.Services.GeoCache
{
    public interface IGeoCache
    {
        IReadOnlyList<Country> GetCountries();
        IReadOnlyList<State> GetStatesByCountry(int countryId);
        IReadOnlyList<City> GetCitiesByState(int stateId);
        void Load();
    }
}
