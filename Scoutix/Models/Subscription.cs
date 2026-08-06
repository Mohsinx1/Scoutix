using System;
using System.Collections.Generic;

namespace Scoutix.Models;

public partial class Subscription
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    public int? PlanId { get; set; }

    public bool? IsSubscriptionActive { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string? PaddleSubscriptionId { get; set; }

    public string SubscriptionStatus { get; set; } = null!;

    public DateTime? CanceledAt { get; set; }

    public DateTime? PausedAt { get; set; }

    public virtual Plan? Plan { get; set; }

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

    public virtual User? User { get; set; }
}
