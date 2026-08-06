using System;
using System.Collections.Generic;

namespace Scoutix.Models;

public partial class Transaction
{
    public int Id { get; set; }

    public int? SubscriptionId { get; set; }

    public int? UserId { get; set; }

    public DateTime? PaymentDate { get; set; }

    public string? PaymentStatus { get; set; }

    public string? PaymentMethod { get; set; }

    public string? TransactionId { get; set; }

    public decimal? PaymentGatewayFee { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public decimal? Amount { get; set; }

    public string? Currency { get; set; }

    public int? PlanId { get; set; }

    public DateTime? BillingPeriodStart { get; set; }

    public DateTime? BillingPeriodEnd { get; set; }

    public virtual Plan? Plan { get; set; }

    public virtual Subscription? Subscription { get; set; }

    public virtual User? User { get; set; }
}
