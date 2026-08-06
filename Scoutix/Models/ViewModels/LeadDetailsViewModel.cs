using Scoutix.Models.Enums;

namespace Scoutix.Models.ViewModels
{
    public class LeadDetailsViewModel
    {
        public int Id { get; set; }
        public int UserLeadId { get; set; }

        public string? Name { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? Address { get; set; }

        public int? NicheId { get; set; }
        public string? Niche { get; set; }

        public LeadStatus Status { get; set; }
        public string? Notes { get; set; }
        public DateTime? FollowUpDate { get; set; }

        public string? CountryName { get; set; }
        public string? StateName { get; set; }
        public string? CityName { get; set; }

        public string? Source { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}