using System;
using System.Collections.Generic;

namespace Scoutix.Models;

public partial class Nich
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public bool? IsActive { get; set; }

    public virtual ICollection<Lead> Leads { get; set; } = new List<Lead>();
}
