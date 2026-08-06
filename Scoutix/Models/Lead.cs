using System;
using System.Collections.Generic;

namespace Scoutix.Models;

public partial class Lead
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Website { get; set; }

    public string? Address { get; set; }

    public int? CountryId { get; set; }

    public int? StateId { get; set; }

    public int? CityId { get; set; }

    public int? NicheId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string? Source { get; set; }

    public int EnrichmentStatus { get; set; }

    public DateTime? EnrichedAt { get; set; }

    public int EnrichmentAttempts { get; set; }

    // Owner-enrichment (Milestone 2)
    public string? OwnerName { get; set; }

    public bool OwnerVerified { get; set; }

    public double OwnerConfidence { get; set; }

    public string? OwnerSources { get; set; }

    public int EmailStatus { get; set; }

    public virtual City? City { get; set; }

    public virtual Country? Country { get; set; }

    public virtual Nich? Niche { get; set; }

    public virtual State? State { get; set; }

    public virtual ICollection<UserLead> UserLeads { get; set; } = new List<UserLead>();
}
