using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Scoutix.Models;

public partial class ApplicationDbContext : DbContext
{
    public ApplicationDbContext()
    {
    }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<City> Cities { get; set; }

    public virtual DbSet<Country> Countries { get; set; }

    public virtual DbSet<Lead> Leads { get; set; }

    public virtual DbSet<Nich> Niches { get; set; }

    public virtual DbSet<NicheGeoProgress> NicheGeoProgresses { get; set; }

    public virtual DbSet<Plan> Plans { get; set; }

    public virtual DbSet<State> States { get; set; }

    public virtual DbSet<Subscription> Subscriptions { get; set; }

    public virtual DbSet<SubscriptionHistory> SubscriptionHistories { get; set; }

    public virtual DbSet<Transaction> Transactions { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserLead> UserLeads { get; set; }

    public virtual DbSet<WebhookLog> WebhookLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<City>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CountryCode).HasMaxLength(50);
            entity.Property(e => e.Latitude).HasColumnType("decimal(18, 10)");
            entity.Property(e => e.Longitude).HasColumnType("decimal(18, 10)");
            entity.Property(e => e.StateCode).HasMaxLength(50);
            entity.Property(e => e.Timezone).HasMaxLength(255);
            entity.Property(e => e.WikiDataId).HasMaxLength(255);

            entity.HasOne(d => d.Country).WithMany(p => p.Cities)
                .HasForeignKey(d => d.CountryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Cities_Countries");

            entity.HasOne(d => d.State).WithMany(p => p.Cities)
                .HasForeignKey(d => d.StateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Cities_States");
        });

        modelBuilder.Entity<Country>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Capital).HasMaxLength(50);
            entity.Property(e => e.Currency).HasMaxLength(50);
            entity.Property(e => e.Iso2)
                .HasMaxLength(50)
                .HasColumnName("ISO2");
            entity.Property(e => e.Iso3)
                .HasMaxLength(50)
                .HasColumnName("ISO3");
            entity.Property(e => e.Name).HasMaxLength(50);
            entity.Property(e => e.Region).HasMaxLength(50);
            entity.Property(e => e.WikiDataId).HasMaxLength(50);
        });

        modelBuilder.Entity<Lead>(entity =>
        {
            entity.HasIndex(e => e.CityId, "IX_Leads_CityId");

            entity.HasIndex(e => e.CountryId, "IX_Leads_CountryId");

            entity.HasIndex(e => new { e.CountryId, e.StateId, e.CityId }, "IX_Leads_CountryId_StateId_CityId");

            entity.HasIndex(e => e.EnrichmentStatus, "IX_Leads_EnrichmentStatus");

            entity.HasIndex(e => e.NicheId, "IX_Leads_NicheId");

            entity.HasIndex(e => e.StateId, "IX_Leads_StateId");

            entity.Property(e => e.EnrichedAt).HasColumnType("datetime");

            entity.HasOne(d => d.City).WithMany(p => p.Leads)
                .HasForeignKey(d => d.CityId)
                .HasConstraintName("FK_Leads_Cities");

            entity.HasOne(d => d.Country).WithMany(p => p.Leads)
                .HasForeignKey(d => d.CountryId)
                .HasConstraintName("FK_Leads_Countries");

            entity.HasOne(d => d.Niche).WithMany(p => p.Leads)
                .HasForeignKey(d => d.NicheId)
                .HasConstraintName("FK_Leads_Niches");

            entity.HasOne(d => d.State).WithMany(p => p.Leads)
                .HasForeignKey(d => d.StateId)
                .HasConstraintName("FK_Leads_States");
        });

        modelBuilder.Entity<Nich>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Niches__57FA59C2ED8A12D7");

            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(255);
        });

        modelBuilder.Entity<NicheGeoProgress>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__NicheGeo__3214EC07A5B1880C");

            entity.ToTable("NicheGeoProgress");

            entity.HasIndex(e => new { e.NicheId, e.CountryId, e.StateId, e.CityId }, "UX_NicheGeoProgress_Unique").IsUnique();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<Plan>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Plans__3214EC07C49742AA");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Currency)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.Description).HasColumnType("text");
            entity.Property(e => e.Features).HasColumnType("text");
            entity.Property(e => e.PaddlePriceId)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.PlanName)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Price).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
        });

        modelBuilder.Entity<State>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CountryCode).HasMaxLength(50);
            entity.Property(e => e.Iso2)
                .HasMaxLength(50)
                .HasColumnName("ISO2");
            entity.Property(e => e.Iso31662)
                .HasMaxLength(50)
                .HasColumnName("ISO3166_2");
            entity.Property(e => e.Latitude).HasColumnType("decimal(18, 10)");
            entity.Property(e => e.Longitude).HasColumnType("decimal(18, 10)");
            entity.Property(e => e.Name).HasMaxLength(50);

            entity.HasOne(d => d.Country).WithMany(p => p.States)
                .HasForeignKey(d => d.CountryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_States_Countries");
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Subscrip__3214EC078946A16B");

            entity.Property(e => e.CanceledAt).HasColumnType("datetime");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())", "DF__Subscript__Creat__62E4AA3C")
                .HasColumnType("datetime");
            entity.Property(e => e.EndDate).HasColumnType("datetime");
            entity.Property(e => e.PaddleSubscriptionId).HasMaxLength(100);
            entity.Property(e => e.PausedAt).HasColumnType("datetime");
            entity.Property(e => e.StartDate).HasColumnType("datetime");
            entity.Property(e => e.SubscriptionStatus)
                .HasMaxLength(50)
                .HasDefaultValue("active", "DF__Subscript__Subsc__1D114BD1");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())", "DF__Subscript__Updat__63D8CE75")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Plan).WithMany(p => p.Subscriptions)
                .HasForeignKey(d => d.PlanId)
                .HasConstraintName("FK__Subscript__PlanI__61F08603");

            entity.HasOne(d => d.User).WithMany(p => p.Subscriptions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK__Subscript__UserI__60FC61CA");
        });

        modelBuilder.Entity<SubscriptionHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Subscrip__3214EC07310D4A4D");

            entity.ToTable("SubscriptionHistory");

            entity.HasIndex(e => e.UserId, "IX_SubscriptionHistory_UserId");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.EndDate).HasColumnType("datetime");
            entity.Property(e => e.EventType).HasMaxLength(100);
            entity.Property(e => e.OccurredAt).HasColumnType("datetime");
            entity.Property(e => e.PaddleSubscriptionId).HasMaxLength(100);
            entity.Property(e => e.StartDate).HasColumnType("datetime");
            entity.Property(e => e.Status).HasMaxLength(50);

            entity.HasOne(d => d.Plan).WithMany(p => p.SubscriptionHistories)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SubscriptionHistory_Plans");

            entity.HasOne(d => d.User).WithMany(p => p.SubscriptionHistories)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SubscriptionHistory_Users");
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Transact__3214EC079C66C84B");

            entity.Property(e => e.Amount).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.BillingPeriodEnd).HasColumnType("datetime");
            entity.Property(e => e.BillingPeriodStart).HasColumnType("datetime");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Currency)
                .HasMaxLength(10)
                .HasDefaultValue("USD");
            entity.Property(e => e.PaymentDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.PaymentGatewayFee).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.PaymentMethod)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.PaymentStatus)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.TransactionId)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Plan).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.PlanId)
                .HasConstraintName("FK_Transactions_Plans");

            entity.HasOne(d => d.Subscription).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.SubscriptionId)
                .HasConstraintName("FK__Transacti__Subsc__66B53B20");

            entity.HasOne(d => d.User).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK__Transacti__UserI__67A95F59");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Users__3214EC0765A51FD9");

            entity.HasIndex(e => e.Email, "UQ__Users__A9D10534294B1709").IsUnique();

            entity.Property(e => e.ActiveJobId).HasMaxLength(100);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())", "DF__Users__CreatedAt__2F9A1060")
                .HasColumnType("datetime");
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF__Users__IsActive__318258D2");
            entity.Property(e => e.LastExportAt).HasColumnType("datetime");
            entity.Property(e => e.PaddleCustomerId).HasMaxLength(100);
            entity.Property(e => e.PasswordHash).HasMaxLength(255);
            entity.Property(e => e.PasswordResetToken).HasMaxLength(500);
            entity.Property(e => e.PasswordResetTokenExpiresAt).HasColumnType("datetime");
            entity.Property(e => e.ProfilePicture).HasMaxLength(255);
            entity.Property(e => e.ReferralCode).HasMaxLength(50);
            entity.Property(e => e.ResendRequestTime).HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())", "DF__Users__UpdatedAt__308E3499")
                .HasColumnType("datetime");
            entity.Property(e => e.UserName).HasMaxLength(100);
            entity.Property(e => e.VerificationToken)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.VerificationTokenExpiresAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<UserLead>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__UserLead__3214EC0747FF68C9");

            entity.HasIndex(e => e.FollowUpDate, "IX_UserLeads_FollowUpDate");

            entity.HasIndex(e => e.LeadId, "IX_UserLeads_LeadId");

            entity.HasIndex(e => e.UserId, "IX_UserLeads_UserId");

            entity.HasIndex(e => new { e.UserId, e.LeadId }, "UQ_UserLeads_UserLead").IsUnique();

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.FollowUpDate).HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime");

            entity.HasOne(d => d.Lead).WithMany(p => p.UserLeads)
                .HasForeignKey(d => d.LeadId)
                .HasConstraintName("FK_UserLeads_Leads");

            entity.HasOne(d => d.User).WithMany(p => p.UserLeads)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_UserLeads_Users");
        });

        modelBuilder.Entity<WebhookLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__WebhookL__3214EC079E542EE0");

            entity.HasIndex(e => e.PaddleEventId, "UX_WebhookLogs_PaddleEventId").IsUnique();

            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
            entity.Property(e => e.EventType).HasMaxLength(100);
            entity.Property(e => e.PaddleEventId).HasMaxLength(100);
            entity.Property(e => e.ReceivedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
