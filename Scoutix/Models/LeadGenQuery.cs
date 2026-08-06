namespace Scoutix.Models
{
    public class LeadGenQuery
    {
        public string Query { get; set; } = string.Empty;
        public int CountryId { get; set; }
        public int? StateId { get; set; }
        public int? CityId { get; set; }
        public string CountryIso2 { get; set; } = "US";
    }
}