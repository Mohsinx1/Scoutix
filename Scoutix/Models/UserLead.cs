using System;
using System.Collections.Generic;

namespace Scoutix.Models;

public partial class UserLead
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int LeadId { get; set; }

    public int Status { get; set; }

    public string? Notes { get; set; }

    public DateTime? FollowUpDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Lead Lead { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
