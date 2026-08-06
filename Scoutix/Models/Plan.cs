using System;
using System.Collections.Generic;

namespace Scoutix.Models;

public partial class Plan
{
    public int Id { get; set; }

    public string? PlanName { get; set; }

    public decimal Price { get; set; }

    public string? Currency { get; set; }

    public string? Description { get; set; }

    public string? Features { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string? PaddlePriceId { get; set; }

    public virtual ICollection<SubscriptionHistory> SubscriptionHistories { get; set; } = new List<SubscriptionHistory>();

    public virtual ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
