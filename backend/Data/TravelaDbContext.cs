using Microsoft.EntityFrameworkCore;
using Travela.Models;
using Travela.Api.Models;

namespace Travela.Data
{
    public partial class TravelaDbContext : DbContext
    {
        public TravelaDbContext(
            DbContextOptions<TravelaDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<LegalAcceptance> LegalAcceptances { get; set; }
        public DbSet<AccountEmailMessage> AccountEmailMessages { get; set; }
        public DbSet<TravelNotification> TravelNotifications { get; set; }
        public DbSet<NotificationAttempt> NotificationAttempts { get; set; }
        public DbSet<TripChange> TripChanges { get; set; }
        public DbSet<TripChangeTicket> TripChangeTickets { get; set; }
        public DbSet<Company> Companies { get; set; }
        public DbSet<Bus> Buses { get; set; }
        public DbSet<Terminal> Terminals { get; set; }
       public DbSet<Travela.Api.Models.Route> Routes { get; set; }
        public DbSet<Trip> Trips { get; set; }
        public DbSet<Reservation> Reservations { get; set; }
        public DbSet<PaymentOrder> PaymentOrders { get; set; }
        public DbSet<PurchaseGroup> PurchaseGroups { get; set; }
        public DbSet<Campaign> Campaigns { get; set; }
        public DbSet<ProviderRefundAttempt> ProviderRefundAttempts { get; set; }
        public DbSet<PaymentEvent> PaymentEvents { get; set; }
        public DbSet<LocalPaymentRecord> LocalPaymentRecords { get; set; }
        public DbSet<PaymentNotification> PaymentNotifications { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
      
      protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    ConfigureSegments(modelBuilder);
    modelBuilder.Entity<LegalAcceptance>(entity =>
    {
        entity.Property(a => a.DocumentType).HasMaxLength(40);
        entity.Property(a => a.DocumentVersion).HasMaxLength(40);
        entity.Property(a => a.DocumentHash).HasMaxLength(64);
        entity.Property(a => a.IpAddress).HasMaxLength(45);
        entity.Property(a => a.Context).HasMaxLength(20);
        entity.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<PurchaseGroup>().WithMany().HasForeignKey(a => a.PurchaseGroupId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<PaymentOrder>().WithMany().HasForeignKey(a => a.PaymentOrderId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(a => new { a.UserId, a.Context, a.RequestKey, a.DocumentType })
            .IsUnique().HasFilter(null);
    });
    modelBuilder.Entity<Campaign>(e =>
    {
        e.Property(c => c.Name).HasMaxLength(120); e.Property(c => c.Description).HasMaxLength(1000);
        e.Property(c => c.Code).HasMaxLength(32); e.Property(c => c.DiscountType).HasMaxLength(20);
        e.Property(c => c.Value).HasPrecision(18, 2); e.Property(c => c.MinimumSubtotal).HasPrecision(18, 2);
        e.Property(c => c.MaximumDiscount).HasPrecision(18, 2); e.Property(c => c.Version).IsRowVersion();
        e.HasIndex(c => new { c.CompanyId, c.Code }).IsUnique().HasFilter("[Code] IS NOT NULL");
        e.HasIndex(c => new { c.CompanyId, c.IsActive, c.StartsAt, c.EndsAt });
        e.HasOne<Company>().WithMany().HasForeignKey(c => c.CompanyId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Travela.Api.Models.Route>().WithMany().HasForeignKey(c => c.RouteId).OnDelete(DeleteBehavior.NoAction);
        e.ToTable(t => t.HasCheckConstraint("CK_Campaigns_Rules", "[EndsAt] > [StartsAt] AND [Value] > 0 AND ([DiscountType] = N'Fixed' OR ([DiscountType] = N'Percentage' AND [Value] <= 99)) AND [MinimumSubtotal] >= 0 AND ([MaximumDiscount] IS NULL OR [MaximumDiscount] > 0) AND ([UsageLimit] IS NULL OR [UsageLimit] > 0) AND [PerUserLimit] > 0 AND ([DepartureFrom] IS NULL OR [DepartureUntil] IS NULL OR [DepartureUntil] > [DepartureFrom])"));
    });
    modelBuilder.Entity<PurchaseGroup>(e =>
    {
        e.Property(g => g.OriginalAmount).HasPrecision(18, 2); e.Property(g => g.DiscountAmount).HasPrecision(18, 2);
        e.Property(g => g.CampaignName).HasMaxLength(120); e.Property(g => g.CouponCode).HasMaxLength(32);
        e.HasOne<Campaign>().WithMany().HasForeignKey(g => g.CampaignId).OnDelete(DeleteBehavior.NoAction);
        e.HasIndex(g => new { g.CampaignId, g.PassengerId, g.PaymentStatus, g.HoldExpiresAt });
        e.ToTable(t => t.HasCheckConstraint("CK_PurchaseGroups_Discount", "[DiscountAmount] >= 0 AND [OriginalAmount] = [Amount] + [DiscountAmount]"));
    });
    modelBuilder.Entity<PaymentOrder>(e =>
    {
        e.Property(p => p.OriginalAmount).HasPrecision(18, 2); e.Property(p => p.DiscountAmount).HasPrecision(18, 2);
        e.Property(p => p.CampaignName).HasMaxLength(120); e.Property(p => p.CouponCode).HasMaxLength(32);
        e.ToTable(t => t.HasCheckConstraint("CK_PaymentOrders_Discount", "[DiscountAmount] >= 0 AND [OriginalAmount] = [Amount] + [DiscountAmount]"));
    });
    modelBuilder.Entity<PurchaseGroup>(e =>
    {
        e.Property(g => g.Amount).HasPrecision(18, 2); e.Property(g => g.Currency).HasMaxLength(3);
        e.Property(g => g.RequestHash).HasMaxLength(64); e.Property(g => g.Provider).HasMaxLength(30);
        e.Property(g => g.PaymentStatus).HasMaxLength(20); e.Property(g => g.CheckoutState).HasMaxLength(20);
        e.Property(g => g.ProviderToken).HasMaxLength(200); e.Property(g => g.ProviderPaymentId).HasMaxLength(128); e.Property(g => g.LastError).HasMaxLength(1000);
        e.HasIndex(g => new { g.PassengerId, g.RequestKey }).IsUnique();
        e.HasIndex(g => new { g.Provider, g.ProviderToken }).IsUnique().HasFilter("[ProviderToken] IS NOT NULL");
        e.HasIndex(g => new { g.PaymentStatus, g.NextReconciliationAt });
        e.HasOne<Trip>().WithMany().HasForeignKey(g => g.TripId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne<User>().WithMany().HasForeignKey(g => g.PassengerId).OnDelete(DeleteBehavior.NoAction);
    });
    modelBuilder.Entity<PaymentOrder>().HasOne(p => p.PurchaseGroup).WithMany(g => g.Items).HasForeignKey(p => p.PurchaseGroupId).OnDelete(DeleteBehavior.NoAction);
    modelBuilder.Entity<ProviderRefundAttempt>(e =>
    {
        e.HasIndex(a => a.OrderId).IsUnique(); e.Property(a => a.RefundKey).HasMaxLength(32); e.Property(a => a.Status).HasMaxLength(20); e.Property(a => a.Reference).HasMaxLength(128);
        e.HasOne<PaymentOrder>().WithMany().HasForeignKey(a => a.OrderId).OnDelete(DeleteBehavior.NoAction);
    });
    modelBuilder.Entity<User>().Property(u => u.EmailNotificationsEnabled).HasDefaultValue(true);
    modelBuilder.Entity<TravelNotification>(e =>
    {
        e.Property(n => n.EventKey).HasMaxLength(160); e.Property(n => n.Type).HasMaxLength(40);
        e.Property(n => n.Channel).HasMaxLength(10); e.Property(n => n.Status).HasMaxLength(20);
        e.Property(n => n.Recipient).HasMaxLength(450); e.Property(n => n.Subject).HasMaxLength(200);
        e.Property(n => n.LastError).HasMaxLength(500); e.Property(n => n.ProviderReference).HasMaxLength(100);
        e.HasIndex(n => new { n.ReservationId, n.EventKey, n.Channel }).IsUnique();
        e.HasIndex(n => new { n.Status, n.NextAttemptAt });
        e.HasOne(n => n.Reservation).WithMany().HasForeignKey(n => n.ReservationId).OnDelete(DeleteBehavior.NoAction);
    });
    modelBuilder.Entity<NotificationAttempt>(e =>
    {
        e.Property(a => a.Status).HasMaxLength(30); e.Property(a => a.Error).HasMaxLength(500); e.Property(a => a.Reason).HasMaxLength(1000);
        e.HasOne(a => a.Notification).WithMany().HasForeignKey(a => a.NotificationId).OnDelete(DeleteBehavior.NoAction);
        e.HasIndex(a => new { a.NotificationId, a.CreatedAt });
    });
    modelBuilder.Entity<TripChange>(e =>
    {
        e.Property(c => c.Reason).HasMaxLength(1000); e.Property(c => c.RequestHash).HasMaxLength(64);
        e.HasIndex(c => c.RequestKey).IsUnique(); e.HasIndex(c => new { c.TripId, c.Version }).IsUnique();
        e.HasOne(c => c.Trip).WithMany().HasForeignKey(c => c.TripId).OnDelete(DeleteBehavior.NoAction);
    });
    modelBuilder.Entity<TripChangeTicket>(e =>
    {
        e.HasKey(c => new { c.TripChangeId, c.ReservationId });
        e.HasOne(c => c.TripChange).WithMany().HasForeignKey(c => c.TripChangeId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(c => c.Reservation).WithMany().HasForeignKey(c => c.ReservationId).OnDelete(DeleteBehavior.NoAction);
    });
    modelBuilder.Entity<Bus>().Property(b => b.SeatLayout).HasMaxLength(20).HasDefaultValue("Unspecified");
    modelBuilder.Entity<Trip>().Property(t => t.SeatLayout).HasMaxLength(20).HasDefaultValue("Unspecified");
    modelBuilder.Entity<Trip>().Property(t => t.DepartureTerminalName).HasMaxLength(160);
    modelBuilder.Entity<Trip>().Property(t => t.ArrivalTerminalName).HasMaxLength(160);
    modelBuilder.Entity<Trip>().Property(t => t.DepartureTerminalAddress).HasMaxLength(640);
    modelBuilder.Entity<Trip>().Property(t => t.ArrivalTerminalAddress).HasMaxLength(640);
    modelBuilder.Entity<Trip>().Property(t => t.BoardingInstructions).HasMaxLength(1000);
    modelBuilder.Entity<Trip>().HasOne(t => t.DepartureTerminal).WithMany().HasForeignKey(t => t.DepartureTerminalId).OnDelete(DeleteBehavior.NoAction);
    modelBuilder.Entity<Trip>().HasOne(t => t.ArrivalTerminal).WithMany().HasForeignKey(t => t.ArrivalTerminalId).OnDelete(DeleteBehavior.NoAction);
    modelBuilder.Entity<Terminal>(e =>
    {
        e.Property(t => t.Name).HasMaxLength(160); e.Property(t => t.City).HasMaxLength(100); e.Property(t => t.Address).HasMaxLength(500);
        e.HasIndex(t => new { t.City, t.Name }).IsUnique();
    });

    modelBuilder.Entity<User>().Property(u => u.ActivationPending).HasDefaultValue(false);
    modelBuilder.Entity<User>().Property(u => u.PasswordResetRequired).HasDefaultValue(false);
    modelBuilder.Entity<AccountEmailMessage>(e =>
    {
        e.Property(m => m.Recipient).HasMaxLength(320);
        e.Property(m => m.Subject).HasMaxLength(200);
        e.Property(m => m.TokenHash).HasMaxLength(64);
        e.Property(m => m.Status).HasMaxLength(20);
        e.Property(m => m.LastError).HasMaxLength(500);
        e.HasIndex(m => new { m.UserId, m.CreatedAt });
        e.HasIndex(m => new { m.Status, m.NextAttemptAt });
        e.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.NoAction);
    });

    modelBuilder.Entity<Reservation>(entity =>
    {
        entity.Property(r => r.PassengerGender).HasMaxLength(6);
        entity.ToTable(t => t.HasCheckConstraint("CK_Reservations_PassengerGender", "[PassengerGender] IS NULL OR [PassengerGender] IN (N'Female', N'Male')"));
        entity.Property(r => r.PassengerEmailSnapshot).HasMaxLength(450);
        entity.Property(r => r.PassengerPhoneSnapshot).HasMaxLength(20);
        entity.Property(r => r.PassengerSnapshotSource).HasMaxLength(20);
        entity.Property(r => r.ReservationNumber).HasMaxLength(32);
        entity.Property(r => r.Price).HasPrecision(18, 2);
        entity.Property(r => r.Status).HasMaxLength(20).HasDefaultValue("Active");
        entity.Property(r => r.CancellationReason).HasMaxLength(1000);
        entity.Property(r => r.Kind).HasMaxLength(20).HasDefaultValue("Legacy");
        entity.Property(r => r.Pnr).HasMaxLength(32);
        entity.HasIndex(r => r.Pnr).IsUnique().HasFilter("[Pnr] IS NOT NULL");
        entity.ToTable(t => t.HasCheckConstraint("CK_Reservations_Kind", "([Kind] = N'Legacy' AND [Pnr] IS NULL) OR ([Kind] = N'Hold' AND [Pnr] IS NULL AND [HoldExpiresAt] IS NOT NULL) OR ([Kind] = N'Ticket' AND [Pnr] IS NOT NULL)"));
        entity.HasIndex(r => r.ReservationNumber).IsUnique();
        entity.HasIndex(r => new { r.TripId, r.SeatNumber });
        entity.HasIndex(r => new { r.PassengerId, r.CreatedAt });
        entity.HasIndex(r => new { r.TripId, r.Kind, r.Status });
        entity.HasIndex(r => r.IssuedAt);
        entity.ToTable(t => t.HasCheckConstraint("CK_Reservations_SeatNumber", "[SeatNumber] > 0"));
        entity.ToTable(t => t.HasCheckConstraint("CK_Reservations_Status", "([Status] = N'Active' AND [CancelledAt] IS NULL) OR ([Status] = N'Cancelled' AND [CancelledAt] IS NOT NULL)"));
        entity.HasOne(r => r.Trip).WithMany().HasForeignKey(r => r.TripId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(r => r.Passenger).WithMany().HasForeignKey(r => r.PassengerId).OnDelete(DeleteBehavior.NoAction);
    });

    modelBuilder.Entity<PaymentOrder>(e =>
    {
        e.Property(p => p.Amount).HasPrecision(18, 2);
        e.Property(p => p.RefundAmount).HasPrecision(18, 2);
        e.Property(p => p.Currency).HasMaxLength(3);
        e.Property(p => p.Provider).HasMaxLength(30);
        e.Property(p => p.PaymentStatus).HasMaxLength(20);
        e.Property(p => p.RefundStatus).HasMaxLength(20);
        e.Property(p => p.RefundKey).HasMaxLength(32);
        e.Property(p => p.LastError).HasMaxLength(1000);
        e.Property(p => p.ProviderReference).HasMaxLength(128);
        e.HasIndex(p => new { p.Provider, p.ProviderReference }).IsUnique().HasFilter("[ProviderReference] IS NOT NULL");
        e.ToTable(t => t.HasCheckConstraint("CK_PaymentOrders_Amount", "[Amount] > 0 AND [RefundAmount] >= 0 AND [RefundAmount] <= [Amount]"));
        e.ToTable(t => t.HasCheckConstraint("CK_PaymentOrders_Paid", "[PaymentStatus] <> N'Paid' OR ([PaidAt] IS NOT NULL AND [ProviderReference] IS NOT NULL)"));
        e.ToTable(t => t.HasCheckConstraint("CK_PaymentOrders_Refund", "([RefundStatus] = N'None' AND [RefundAmount] = 0) OR ([PaymentStatus] = N'Paid' AND [RefundAmount] = [Amount] AND ([RefundStatus] IN (N'Pending', N'Failed') OR ([RefundStatus] = N'Succeeded' AND [RefundedAt] IS NOT NULL AND [RefundReference] IS NOT NULL)))"));
        e.HasIndex(p => new { p.PassengerId, p.RequestKey }).IsUnique();
        e.HasIndex(p => p.RefundKey).IsUnique();
        e.HasOne(p => p.Reservation).WithOne(r => r.Payment).HasForeignKey<PaymentOrder>(p => p.ReservationId).OnDelete(DeleteBehavior.NoAction);
        e.HasIndex(p => new { p.RefundStatus, p.NextRefundAttemptAt });
        e.HasIndex(p => new { p.PaymentStatus, p.NextReconciliationAt });
        e.HasIndex(p => new { p.IsTest, p.PaidAt });
        e.HasIndex(p => new { p.IsTest, p.RefundStatusChangedAt });
        e.HasIndex(p => new { p.IsTest, p.RefundedAt });
    });
    modelBuilder.Entity<PaymentEvent>(e =>
    {
        e.Property(p => p.Type).HasMaxLength(50);
        e.Property(p => p.PaymentStatus).HasMaxLength(20);
        e.Property(p => p.RefundStatus).HasMaxLength(20);
        e.Property(p => p.Reason).HasMaxLength(1000);
        e.Property(p => p.RefundAmount).HasPrecision(18, 2);
        e.HasIndex(p => new { p.OrderId, p.CreatedAt });
        e.HasIndex(p => new { p.ActorUserId, p.RequestKey }).IsUnique().HasFilter("[RequestKey] IS NOT NULL");
        e.HasOne(p => p.Order).WithMany().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.NoAction);
    });
    modelBuilder.Entity<LocalPaymentRecord>(e =>
    {
        e.Property(p => p.Amount).HasPrecision(18, 2);
        e.Property(p => p.EventId).HasMaxLength(32);
        e.Property(p => p.Currency).HasMaxLength(3);
        e.Property(p => p.Status).HasMaxLength(20);
        e.Property(p => p.RefundOutcome).HasMaxLength(20);
        e.Property(p => p.RefundKey).HasMaxLength(32);
        e.ToTable(t => t.HasCheckConstraint("CK_LocalPaymentRecords_RefundTransfers", "[RefundTransfers] IN (0, 1)"));
    });
    modelBuilder.Entity<PaymentNotification>(e =>
    {
        e.Property(p => p.Provider).HasMaxLength(30);
        e.Property(p => p.EventId).HasMaxLength(100);
        e.HasIndex(p => new { p.Provider, p.EventId }).IsUnique();
    });

    modelBuilder.Entity<User>()
        .HasIndex(u => u.Email)
        .IsUnique();

    modelBuilder.Entity<User>()
        .HasOne<Company>()
        .WithMany()
        .HasForeignKey(u => u.CompanyId)
        .OnDelete(DeleteBehavior.SetNull);

    modelBuilder.Entity<Trip>()
        .Property(t => t.Price)
        .HasPrecision(18, 2);

    modelBuilder.Entity<Trip>()
        .Property(t => t.CancellationReason)
        .HasMaxLength(500);

    modelBuilder.Entity<Trip>()
        .HasIndex(t => new { t.BusId, t.DepartureTime });
    modelBuilder.Entity<Trip>().HasIndex(t => new { t.CompanyId, t.DepartureTime });

    modelBuilder.Entity<Bus>()
        .Property(b => b.PlateNumber)
        .HasMaxLength(20);

    modelBuilder.Entity<Bus>()
        .Property(b => b.NormalizedPlateNumber)
        .HasMaxLength(20);

    modelBuilder.Entity<Bus>()
        .HasIndex(b => b.NormalizedPlateNumber)
        .IsUnique();

    modelBuilder.Entity<Trip>()
        .HasOne(t => t.Company)
        .WithMany()
        .HasForeignKey(t => t.CompanyId)
        .OnDelete(DeleteBehavior.NoAction);

    modelBuilder.Entity<RolePermission>()
        .HasOne(rp => rp.Role)
        .WithMany()
        .HasForeignKey(rp => rp.RoleId)
        .OnDelete(DeleteBehavior.Cascade);

    modelBuilder.Entity<RolePermission>()
        .HasOne(rp => rp.Permission)
        .WithMany()
        .HasForeignKey(rp => rp.PermissionId)
        .OnDelete(DeleteBehavior.Cascade);

    modelBuilder.Entity<RolePermission>()
        .HasIndex(rp => new { rp.RoleId, rp.PermissionId })
        .IsUnique();

    modelBuilder.Entity<Trip>()
        .HasOne(t => t.Bus)
        .WithMany()
        .HasForeignKey(t => t.BusId)
        .OnDelete(DeleteBehavior.NoAction);

    modelBuilder.Entity<Trip>()
        .HasOne(t => t.Route)
        .WithMany()
        .HasForeignKey(t => t.RouteId)
        .OnDelete(DeleteBehavior.NoAction);

    modelBuilder.Entity<Trip>()
        .HasOne<User>()
        .WithMany()
        .HasForeignKey(t => t.CancelledByUserId)
        .OnDelete(DeleteBehavior.SetNull);
}
    }
}
