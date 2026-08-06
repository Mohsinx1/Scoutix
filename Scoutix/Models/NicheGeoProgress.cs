using System;
using System.Collections.Generic;

namespace Scoutix.Models;

public partial class NicheGeoProgress
{
    public int Id { get; set; }

    public int NicheId { get; set; }

    public int CountryId { get; set; }

    public int? StateId { get; set; }

    public int? CityId { get; set; }

    public int LeadsCollected { get; set; }

    public bool IsExhausted { get; set; }

    public bool IsInProgress { get; set; }

    public DateTime? LockedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastScrapedAt { get; set; }
}
