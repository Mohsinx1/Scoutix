using System;
using System.Collections.Generic;

namespace Scoutix.Models;

public partial class City
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public int StateId { get; set; }

    public string? StateCode { get; set; }

    public int CountryId { get; set; }

    public string? CountryCode { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public string? Timezone { get; set; }

    public string? WikiDataId { get; set; }

    public virtual Country Country { get; set; } = null!;

    public virtual ICollection<Lead> Leads { get; set; } = new List<Lead>();

    public virtual State State { get; set; } = null!;
}
