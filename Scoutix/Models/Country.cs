using System;
using System.Collections.Generic;

namespace Scoutix.Models;

public partial class Country
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Iso3 { get; set; }

    public string? Iso2 { get; set; }

    public short? PhoneCode { get; set; }

    public string? Capital { get; set; }

    public string? Currency { get; set; }

    public string? Region { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? WikiDataId { get; set; }

    public virtual ICollection<City> Cities { get; set; } = new List<City>();

    public virtual ICollection<Lead> Leads { get; set; } = new List<Lead>();

    public virtual ICollection<State> States { get; set; } = new List<State>();
}
