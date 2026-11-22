using CampusVenue.Api.Modules.Facilities.Domain;
using CampusVenue.Api.Modules.Identity.Domain;
using CampusVenue.Api.Modules.Reservations.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CampusVenue.Api.Infrastructure.Data;

/// <summary>
/// Primary Entity Framework Core database context for CampusVenue.
/// Hosts Identity and cross-module relational persistence with snake_case naming conventions.
/// </summary>
public class CampusVenueDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public CampusVenueDbContext(DbContextOptions<CampusVenueDbContext> options)
        : base(options)
    {
    }

    public DbSet<RoleAssignmentAudit> RoleAssignmentAudits => Set<RoleAssignmentAudit>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<VenueManager> VenueManagers => Set<VenueManager>();
    public DbSet<VenueCalendarCoverage> VenueCalendarCoverages => Set<VenueCalendarCoverage>();
    public DbSet<VenueCalendarBlock> VenueCalendarBlocks => Set<VenueCalendarBlock>();
    public DbSet<VenueBookingRequest> VenueBookingRequests => Set<VenueBookingRequest>();
    public DbSet<BookingNotification> BookingNotifications => Set<BookingNotification>();
    public DbSet<ConfirmedReservation> ConfirmedReservations => Set<ConfirmedReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PostgreSQL btree_gist extension for GiST exclusion constraints on temporal ranges
        modelBuilder.HasPostgresExtension("btree_gist");

        // Map ASP.NET Core Identity tables to snake_case plural names
        modelBuilder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("users");
            b.Property(u => u.Id).HasColumnName("id");
            b.Property(u => u.UserName).HasColumnName("user_name").HasMaxLength(256);
            b.Property(u => u.NormalizedUserName).HasColumnName("normalized_user_name").HasMaxLength(256);
            b.Property(u => u.Email).HasColumnName("email").HasMaxLength(256);
            b.Property(u => u.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(256);
            b.Property(u => u.EmailConfirmed).HasColumnName("email_confirmed");
            b.Property(u => u.PasswordHash).HasColumnName("password_hash");
            b.Property(u => u.SecurityStamp).HasColumnName("security_stamp");
            b.Property(u => u.ConcurrencyStamp).HasColumnName("concurrency_stamp");
            b.Property(u => u.PhoneNumber).HasColumnName("phone_number");
            b.Property(u => u.PhoneNumberConfirmed).HasColumnName("phone_number_confirmed");
            b.Property(u => u.TwoFactorEnabled).HasColumnName("two_factor_enabled");
            b.Property(u => u.LockoutEnd).HasColumnName("lockout_end");
            b.Property(u => u.LockoutEnabled).HasColumnName("lockout_enabled");
            b.Property(u => u.AccessFailedCount).HasColumnName("access_failed_count");
            b.Property(u => u.CreatedAtUtc).HasColumnName("created_at_utc");
            b.Property(u => u.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<ApplicationRole>(b =>
        {
            b.ToTable("roles");
            b.Property(r => r.Id).HasColumnName("id");
            b.Property(r => r.Name).HasColumnName("name").HasMaxLength(256);
            b.Property(r => r.NormalizedName).HasColumnName("normalized_name").HasMaxLength(256);
            b.Property(r => r.ConcurrencyStamp).HasColumnName("concurrency_stamp");
            b.Property(r => r.Description).HasColumnName("description").HasMaxLength(500);
            b.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc");
        });

        modelBuilder.Entity<IdentityUserRole<Guid>>(b =>
        {
            b.ToTable("user_roles");
            b.Property(ur => ur.UserId).HasColumnName("user_id");
            b.Property(ur => ur.RoleId).HasColumnName("role_id");
        });

        modelBuilder.Entity<IdentityUserClaim<Guid>>(b =>
        {
            b.ToTable("user_claims");
            b.Property(uc => uc.Id).HasColumnName("id");
            b.Property(uc => uc.UserId).HasColumnName("user_id");
            b.Property(uc => uc.ClaimType).HasColumnName("claim_type");
            b.Property(uc => uc.ClaimValue).HasColumnName("claim_value");
        });

        modelBuilder.Entity<IdentityUserLogin<Guid>>(b =>
        {
            b.ToTable("user_logins");
            b.Property(ul => ul.LoginProvider).HasColumnName("login_provider");
            b.Property(ul => ul.ProviderKey).HasColumnName("provider_key");
            b.Property(ul => ul.ProviderDisplayName).HasColumnName("provider_display_name");
            b.Property(ul => ul.UserId).HasColumnName("user_id");
        });

        modelBuilder.Entity<IdentityRoleClaim<Guid>>(b =>
        {
            b.ToTable("role_claims");
            b.Property(rc => rc.Id).HasColumnName("id");
            b.Property(rc => rc.RoleId).HasColumnName("role_id");
            b.Property(rc => rc.ClaimType).HasColumnName("claim_type");
            b.Property(rc => rc.ClaimValue).HasColumnName("claim_value");
        });

        modelBuilder.Entity<IdentityUserToken<Guid>>(b =>
        {
            b.ToTable("user_tokens");
            b.Property(ut => ut.UserId).HasColumnName("user_id");
            b.Property(ut => ut.LoginProvider).HasColumnName("login_provider");
            b.Property(ut => ut.Name).HasColumnName("name");
            b.Property(ut => ut.Value).HasColumnName("value");
        });

        modelBuilder.Entity<RoleAssignmentAudit>(b =>
        {
            b.ToTable("role_assignment_audits");
            b.HasKey(a => a.Id).HasName("pk_role_assignment_audits");
            b.Property(a => a.Id).HasColumnName("id");
            b.Property(a => a.ActorId).HasColumnName("actor_id").IsRequired();
            b.Property(a => a.TargetUserId).HasColumnName("target_user_id").IsRequired();
            b.Property(a => a.PreviousRoles).HasColumnName("previous_roles").HasColumnType("text[]").IsRequired();
            b.Property(a => a.NewRoles).HasColumnName("new_roles").HasColumnType("text[]").IsRequired();
            b.Property(a => a.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();

            b.HasOne(a => a.Actor)
                .WithMany()
                .HasForeignKey(a => a.ActorId)
                .HasConstraintName("fk_role_assignment_audits_users_actor_id")
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(a => a.TargetUser)
                .WithMany()
                .HasForeignKey(a => a.TargetUserId)
                .HasConstraintName("fk_role_assignment_audits_users_target_user_id")
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(a => a.TargetUserId).HasDatabaseName("ix_role_assignment_audits_target_user_id");
            b.HasIndex(a => a.ActorId).HasDatabaseName("ix_role_assignment_audits_actor_id");
            b.HasIndex(a => a.CreatedAtUtc).HasDatabaseName("ix_role_assignment_audits_created_at_utc");
        });

        modelBuilder.Entity<Venue>(b =>
        {
            b.ToTable("venues", t =>
            {
                t.HasCheckConstraint("ck_venues_capacity_positive", "capacity > 0");
                t.HasCheckConstraint("ck_venues_min_notice_positive", "min_notice_hours >= 0");
                t.HasCheckConstraint("ck_venues_setup_buffer_nonnegative", "default_setup_minutes >= 0");
                t.HasCheckConstraint("ck_venues_cleanup_buffer_nonnegative", "default_cleanup_minutes >= 0");
                t.HasCheckConstraint("ck_venues_max_booking_positive", "max_booking_hours > 0");
            });

            b.HasKey(v => v.Id).HasName("pk_venues");
            b.Property(v => v.Id).HasColumnName("id");
            b.Property(v => v.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            b.Property(v => v.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
            b.Property(v => v.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
            b.Property(v => v.ManagingUnit).HasColumnName("managing_unit").HasMaxLength(200).IsRequired();
            b.Property(v => v.Capacity).HasColumnName("capacity").IsRequired();
            b.Property(v => v.OperatingHoursStart).HasColumnName("operating_hours_start").IsRequired();
            b.Property(v => v.OperatingHoursEnd).HasColumnName("operating_hours_end").IsRequired();
            b.Property(v => v.MinNoticeHours).HasColumnName("min_notice_hours").IsRequired();
            b.Property(v => v.DefaultSetupMinutes).HasColumnName("default_setup_minutes").IsRequired();
            b.Property(v => v.DefaultCleanupMinutes).HasColumnName("default_cleanup_minutes").IsRequired();
            b.Property(v => v.MaxBookingHours).HasColumnName("max_booking_hours").IsRequired();
            b.Property(v => v.RequiresEventApproval).HasColumnName("requires_event_approval").IsRequired();
            b.Property(v => v.GeneralRules).HasColumnName("general_rules").HasMaxLength(4000);
            b.Property(v => v.IsActive).HasColumnName("is_active").IsRequired();
            b.Property(v => v.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();
            b.Property(v => v.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamp with time zone").IsRequired();

            b.HasIndex(v => v.Code).IsUnique().HasDatabaseName("ix_venues_code");
            b.HasIndex(v => v.IsActive).HasDatabaseName("ix_venues_is_active");
        });

        modelBuilder.Entity<VenueManager>(b =>
        {
            b.ToTable("venue_managers");
            b.HasKey(vm => new { vm.VenueId, vm.UserId }).HasName("pk_venue_managers");
            b.Property(vm => vm.VenueId).HasColumnName("venue_id");
            b.Property(vm => vm.UserId).HasColumnName("user_id");
            b.Property(vm => vm.AssignedAtUtc).HasColumnName("assigned_at_utc").HasColumnType("timestamp with time zone").IsRequired();

            b.HasOne(vm => vm.Venue)
                .WithMany(v => v.Managers)
                .HasForeignKey(vm => vm.VenueId)
                .HasConstraintName("fk_venue_managers_venues_venue_id")
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(vm => vm.User)
                .WithMany()
                .HasForeignKey(vm => vm.UserId)
                .HasConstraintName("fk_venue_managers_users_user_id")
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(vm => vm.UserId).HasDatabaseName("ix_venue_managers_user_id");
        });

        modelBuilder.Entity<VenueCalendarCoverage>(b =>
        {
            b.ToTable("venue_calendar_coverages", t =>
            {
                t.HasCheckConstraint("ck_venue_calendar_coverages_dates", "covers_to_utc > covers_from_utc");
            });

            b.HasKey(c => c.Id).HasName("pk_venue_calendar_coverages");
            b.Property(c => c.Id).HasColumnName("id");
            b.Property(c => c.VenueId).HasColumnName("venue_id").IsRequired();
            b.Property(c => c.SourceName).HasColumnName("source_name").HasMaxLength(200).IsRequired();
            b.Property(c => c.SourceReference).HasColumnName("source_reference").HasMaxLength(200);
            b.Property(c => c.CoversFromUtc).HasColumnName("covers_from_utc").HasColumnType("timestamp with time zone").IsRequired();
            b.Property(c => c.CoversToUtc).HasColumnName("covers_to_utc").HasColumnType("timestamp with time zone").IsRequired();
            b.Property(c => c.VerifiedByUserId).HasColumnName("verified_by_user_id").IsRequired();
            b.Property(c => c.VerifiedAtUtc).HasColumnName("verified_at_utc").HasColumnType("timestamp with time zone").IsRequired();
            b.Property(c => c.Notes).HasColumnName("notes").HasMaxLength(1000);
            b.Property(c => c.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();

            b.HasOne(c => c.Venue)
                .WithMany(v => v.CalendarCoverages)
                .HasForeignKey(c => c.VenueId)
                .HasConstraintName("fk_venue_calendar_coverages_venues_venue_id")
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(c => c.VerifiedByUser)
                .WithMany()
                .HasForeignKey(c => c.VerifiedByUserId)
                .HasConstraintName("fk_venue_calendar_coverages_users_verified_by_user_id")
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(c => new { c.VenueId, c.CoversFromUtc, c.CoversToUtc }).HasDatabaseName("ix_venue_calendar_coverages_venue_id_coverage");
            b.HasIndex(c => c.VerifiedByUserId).HasDatabaseName("ix_venue_calendar_coverages_verified_by_user_id");
        });

        modelBuilder.Entity<VenueCalendarBlock>(b =>
        {
            b.ToTable("venue_calendar_blocks", t =>
            {
                t.HasCheckConstraint("ck_venue_calendar_blocks_dates", "end_utc > start_utc");
            });

            b.HasKey(b => b.Id).HasName("pk_venue_calendar_blocks");
            b.Property(b => b.Id).HasColumnName("id");
            b.Property(b => b.VenueId).HasColumnName("venue_id").IsRequired();
            b.Property(b => b.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
            b.Property(b => b.BlockType).HasColumnName("block_type").HasMaxLength(50).IsRequired();
            b.Property(b => b.StartUtc).HasColumnName("start_utc").HasColumnType("timestamp with time zone").IsRequired();
            b.Property(b => b.EndUtc).HasColumnName("end_utc").HasColumnType("timestamp with time zone").IsRequired();
            b.Property(b => b.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
            b.Property(b => b.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();

            b.HasOne(b => b.Venue)
                .WithMany(v => v.CalendarBlocks)
                .HasForeignKey(b => b.VenueId)
                .HasConstraintName("fk_venue_calendar_blocks_venues_venue_id")
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(b => b.CreatedByUser)
                .WithMany()
                .HasForeignKey(b => b.CreatedByUserId)
                .HasConstraintName("fk_venue_calendar_blocks_users_created_by_user_id")
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(b => new { b.VenueId, b.StartUtc, b.EndUtc }).HasDatabaseName("ix_venue_calendar_blocks_venue_id_dates");
            b.HasIndex(b => b.CreatedByUserId).HasDatabaseName("ix_venue_calendar_blocks_created_by_user_id");
        });

        modelBuilder.Entity<VenueBookingRequest>(b =>
        {
            b.ToTable("venue_booking_requests", t =>
            {
                t.HasCheckConstraint("ck_venue_booking_requests_dates", "event_end_utc > event_start_utc");
                t.HasCheckConstraint("ck_venue_booking_requests_attendance", "expected_attendance > 0");
                t.HasCheckConstraint("ck_venue_booking_requests_buffers", "setup_minutes >= 0 AND cleanup_minutes >= 0");
            });

            b.HasKey(r => r.Id).HasName("pk_venue_booking_requests");
            b.Property(r => r.Id).HasColumnName("id");
            b.Property(r => r.VenueId).HasColumnName("venue_id").IsRequired();
            b.Property(r => r.RequesterId).HasColumnName("requester_id").IsRequired();
            b.Property(r => r.EventTitle).HasColumnName("event_title").HasMaxLength(200).IsRequired();
            b.Property(r => r.Description).HasColumnName("description").HasMaxLength(2000);
            b.Property(r => r.ExpectedAttendance).HasColumnName("expected_attendance").IsRequired();
            b.Property(r => r.EventStartUtc).HasColumnName("event_start_utc").HasColumnType("timestamp with time zone").IsRequired();
            b.Property(r => r.EventEndUtc).HasColumnName("event_end_utc").HasColumnType("timestamp with time zone").IsRequired();
            b.Property(r => r.SetupMinutes).HasColumnName("setup_minutes").IsRequired();
            b.Property(r => r.CleanupMinutes).HasColumnName("cleanup_minutes").IsRequired();
            b.Property(r => r.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            b.Property(r => r.ReviewNotes).HasColumnName("review_notes").HasMaxLength(2000);
            b.Property(r => r.Conditions).HasColumnName("conditions").HasMaxLength(2000);
            b.Property(r => r.ConditionsAccepted).HasColumnName("conditions_accepted");
            b.Property(r => r.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
            b.Property(r => r.ReviewedAtUtc).HasColumnName("reviewed_at_utc").HasColumnType("timestamp with time zone");
            b.Property(r => r.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100);
            b.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();
            b.Property(r => r.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamp with time zone").IsRequired();

            b.HasOne(r => r.Venue)
                .WithMany()
                .HasForeignKey(r => r.VenueId)
                .HasConstraintName("fk_venue_booking_requests_venues_venue_id")
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(r => r.Requester)
                .WithMany()
                .HasForeignKey(r => r.RequesterId)
                .HasConstraintName("fk_venue_booking_requests_users_requester_id")
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(r => r.ReviewedByUser)
                .WithMany()
                .HasForeignKey(r => r.ReviewedByUserId)
                .HasConstraintName("fk_venue_booking_requests_users_reviewed_by_user_id")
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(r => r.VenueId).HasDatabaseName("ix_venue_booking_requests_venue_id");
            b.HasIndex(r => r.RequesterId).HasDatabaseName("ix_venue_booking_requests_requester_id");
            b.HasIndex(r => r.Status).HasDatabaseName("ix_venue_booking_requests_status");
            b.HasIndex(r => new { r.VenueId, r.EventStartUtc, r.EventEndUtc }).HasDatabaseName("ix_venue_booking_requests_venue_dates");
            b.HasIndex(r => r.IdempotencyKey).IsUnique().HasFilter("idempotency_key IS NOT NULL").HasDatabaseName("ix_venue_booking_requests_idempotency_key");
        });

        modelBuilder.Entity<BookingNotification>(b =>
        {
            b.ToTable("booking_notifications");
            b.HasKey(n => n.Id).HasName("pk_booking_notifications");
            b.Property(n => n.Id).HasColumnName("id");
            b.Property(n => n.BookingRequestId).HasColumnName("booking_request_id").IsRequired();
            b.Property(n => n.RecipientUserId).HasColumnName("recipient_user_id").IsRequired();
            b.Property(n => n.RecipientEmail).HasColumnName("recipient_email").HasMaxLength(256).IsRequired();
            b.Property(n => n.NotificationType).HasColumnName("notification_type").HasMaxLength(50).IsRequired();
            b.Property(n => n.Subject).HasColumnName("subject").HasMaxLength(300).IsRequired();
            b.Property(n => n.BodyHtml).HasColumnName("body_html").IsRequired();
            b.Property(n => n.SentAtUtc).HasColumnName("sent_at_utc").HasColumnType("timestamp with time zone").IsRequired();
            b.Property(n => n.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();

            b.HasOne(n => n.BookingRequest)
                .WithMany(r => r.Notifications)
                .HasForeignKey(n => n.BookingRequestId)
                .HasConstraintName("fk_booking_notifications_venue_booking_requests_booking_request_id")
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(n => n.RecipientUser)
                .WithMany()
                .HasForeignKey(n => n.RecipientUserId)
                .HasConstraintName("fk_booking_notifications_users_recipient_user_id")
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(n => n.BookingRequestId).HasDatabaseName("ix_booking_notifications_booking_request_id");
            b.HasIndex(n => n.RecipientUserId).HasDatabaseName("ix_booking_notifications_recipient_user_id");
        });

        modelBuilder.Entity<ConfirmedReservation>(b =>
        {
            b.ToTable("confirmed_reservations", t =>
            {
                t.HasCheckConstraint("ck_confirmed_reservations_dates", "event_end_utc > event_start_utc");
                t.HasCheckConstraint("ck_confirmed_reservations_occupied_dates", "occupied_end_utc > occupied_start_utc");
                t.HasCheckConstraint("ck_confirmed_reservations_buffers", "setup_minutes >= 0 AND cleanup_minutes >= 0");
            });

            b.HasKey(r => r.Id).HasName("pk_confirmed_reservations");
            b.Property(r => r.Id).HasColumnName("id");
            b.Property(r => r.BookingRequestId).HasColumnName("booking_request_id").IsRequired();
            b.Property(r => r.VenueId).HasColumnName("venue_id").IsRequired();
            b.Property(r => r.ReservationReference).HasColumnName("reservation_reference").HasMaxLength(50).IsRequired();
            b.Property(r => r.EventTitle).HasColumnName("event_title").HasMaxLength(200).IsRequired();
            b.Property(r => r.EventStartUtc).HasColumnName("event_start_utc").HasColumnType("timestamp with time zone").IsRequired();
            b.Property(r => r.EventEndUtc).HasColumnName("event_end_utc").HasColumnType("timestamp with time zone").IsRequired();
            b.Property(r => r.SetupMinutes).HasColumnName("setup_minutes").IsRequired();
            b.Property(r => r.CleanupMinutes).HasColumnName("cleanup_minutes").IsRequired();
            b.Property(r => r.OccupiedStartUtc).HasColumnName("occupied_start_utc").HasColumnType("timestamp with time zone").IsRequired();
            b.Property(r => r.OccupiedEndUtc).HasColumnName("occupied_end_utc").HasColumnType("timestamp with time zone").IsRequired();
            b.Property(r => r.OccupiedRange).HasColumnName("occupied_range").HasColumnType("tstzrange").IsRequired();
            b.Property(r => r.ConfirmedByUserId).HasColumnName("confirmed_by_user_id").IsRequired();
            b.Property(r => r.ConfirmedAtUtc).HasColumnName("confirmed_at_utc").HasColumnType("timestamp with time zone").IsRequired();
            b.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();

            b.HasOne(r => r.BookingRequest)
                .WithOne(br => br.ConfirmedReservation)
                .HasForeignKey<ConfirmedReservation>(r => r.BookingRequestId)
                .HasConstraintName("fk_confirmed_reservations_venue_booking_requests_booking_request_id")
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(r => r.Venue)
                .WithMany()
                .HasForeignKey(r => r.VenueId)
                .HasConstraintName("fk_confirmed_reservations_venues_venue_id")
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(r => r.ConfirmedByUser)
                .WithMany()
                .HasForeignKey(r => r.ConfirmedByUserId)
                .HasConstraintName("fk_confirmed_reservations_users_confirmed_by_user_id")
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(r => r.BookingRequestId).IsUnique().HasDatabaseName("ix_confirmed_reservations_booking_request_id");
            b.HasIndex(r => r.ReservationReference).IsUnique().HasDatabaseName("ix_confirmed_reservations_reservation_reference");
            b.HasIndex(r => r.VenueId).HasDatabaseName("ix_confirmed_reservations_venue_id");
            b.HasIndex(r => new { r.VenueId, r.OccupiedStartUtc, r.OccupiedEndUtc }).HasDatabaseName("ix_confirmed_reservations_venue_occupied_dates");
        });

        // Apply entity configurations from all module assemblies
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CampusVenueDbContext).Assembly);
    }
}
