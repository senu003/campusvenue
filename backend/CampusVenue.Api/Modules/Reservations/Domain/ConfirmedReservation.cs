using CampusVenue.Api.Modules.Facilities.Domain;
using CampusVenue.Api.Modules.Identity.Domain;
using NpgsqlTypes;

namespace CampusVenue.Api.Modules.Reservations.Domain;

/// <summary>
/// Represents an immutable, legally confirmed venue reservation.
/// Guaranteed against temporal double-bookings via PostgreSQL btree_gist exclusion constraints.
/// </summary>
public class ConfirmedReservation
{
    public Guid Id { get; set; }

    public Guid BookingRequestId { get; set; }
    public VenueBookingRequest BookingRequest { get; set; } = null!;

    public Guid VenueId { get; set; }
    public Venue Venue { get; set; } = null!;

    /// <summary>
    /// Unique human-readable reservation reference code (e.g. RES-20261122-A1B2C3).
    /// </summary>
    public string ReservationReference { get; set; } = string.Empty;

    public string EventTitle { get; set; } = string.Empty;

    public DateTime EventStartUtc { get; set; }
    public DateTime EventEndUtc { get; set; }

    public int SetupMinutes { get; set; }
    public int CleanupMinutes { get; set; }

    public DateTime OccupiedStartUtc { get; set; }
    public DateTime OccupiedEndUtc { get; set; }

    /// <summary>
    /// PostgreSQL tstzrange temporal interval: [OccupiedStartUtc, OccupiedEndUtc).
    /// Used directly in PostgreSQL EXCLUDE USING gist constraints.
    /// </summary>
    public NpgsqlRange<DateTime> OccupiedRange { get; set; }

    public Guid ConfirmedByUserId { get; set; }
    public ApplicationUser ConfirmedByUser { get; set; } = null!;

    public DateTime ConfirmedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
