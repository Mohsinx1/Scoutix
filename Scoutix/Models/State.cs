using System;
using System.Collections.Generic;

namespace Scoutix.Models;

public partial class State
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public int CountryId { get; set; }

    public string? CountryCode { get; set; }

    public string? Iso2 { get; set; }

    public string? Iso31662 { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public string? Timezone { get; set; }

    public string? WikiDataId { get; set; }

    public virtual ICollection<City> Cities { get; set; } = new List<City>();

    public virtual Country Country { get; set; } = null!;

    public virtual ICollection<Lead> Leads { get; set; } = new List<Lead>();
}
