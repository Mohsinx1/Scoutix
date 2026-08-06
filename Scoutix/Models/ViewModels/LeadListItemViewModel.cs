using Scoutix.Models.Enums;
using System;

namespace Scoutix.Models.ViewModels
{
    public class LeadListItemViewModel
    {
        public int Id { get; set; }           // Lead.Id
        public int UserLeadId { get; set; }   // UserLead.Id — use this for status/notes updates

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

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public int? CountryId { get; set; }
        public int? StateId { get; set; }
        public int? CityId { get; set; }
        public string? CountryName { get; set; }
        public string? StateName { get; set; }
        public string? CityName { get; set; }

        public string? Source { get; set; }

        public int EnrichmentStatus { get; set; }
        public int EnrichmentAttempts { get; set; }

        public string? OwnerName { get; set; }
        public bool OwnerVerified { get; set; }
        public int EmailStatus { get; set; }
    }
}