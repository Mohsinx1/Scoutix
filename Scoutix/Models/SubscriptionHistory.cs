using System;
using System.Collections.Generic;

namespace Scoutix.Models;

public partial class SubscriptionHistory
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int PlanId { get; set; }

    public string? PaddleSubscriptionId { get; set; }

    public string EventType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public DateTime OccurredAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Plan Plan { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
