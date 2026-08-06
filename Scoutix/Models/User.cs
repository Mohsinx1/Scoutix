using System;
using System.Collections.Generic;

namespace Scoutix.Models;

public partial class User
{
    public int Id { get; set; }

    public string UserName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public bool? IsActive { get; set; }

    public bool? IsEmailVerified { get; set; }

    public bool AgreeToTerms { get; set; }

    public string? ProfilePicture { get; set; }

    public string? ReferralCode { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string? VerificationToken { get; set; }

    public DateTime? ResendRequestTime { get; set; }

    public int? PlanId { get; set; }

    public int? SubscriptionId { get; set; }

    public string? PaddleCustomerId { get; set; }

    public int FailedLoginCount { get; set; }

    public bool IsLockedOut { get; set; }

    public string? PasswordResetToken { get; set; }

    public DateTime? PasswordResetTokenExpiresAt { get; set; }

    public DateTime? VerificationTokenExpiresAt { get; set; }

    public string? ActiveJobId { get; set; }

    public int LeadGenBaseCount { get; set; }

    public DateTime? LastExportAt { get; set; }

    public virtual ICollection<SubscriptionHistory> SubscriptionHistories { get; set; } = new List<SubscriptionHistory>();

    public virtual ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

    public virtual ICollection<UserLead> UserLeads { get; set; } = new List<UserLead>();
}
