using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Modules.Facilities.Domain;
using Microsoft.EntityFrameworkCore;

namespace CampusVenue.Api.Modules.Facilities.Features.Availability;

public class AvailabilityService
{
    private static readonly TimeSpan SriLankaOffset = TimeSpan.FromHours(5.5);

    public async Task<CheckAvailabilityResponse> CheckAvailabilityAsync(
        Venue venue,
        CheckAvailabilityRequest request,
        CampusVenueDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var eventStartUtc = DateTime.SpecifyKind(request.EventStartUtc, DateTimeKind.Utc);
        var eventEndUtc = DateTime.SpecifyKind(request.EventEndUtc, DateTimeKind.Utc);

        if (eventEndUtc <= eventStartUtc)
        {
            throw new ArgumentException("Event end time must be strictly after event start time.");
        }

        var setupMinutes = request.SetupMinutes ?? venue.DefaultSetupMinutes;
        var cleanupMinutes = request.CleanupMinutes ?? venue.DefaultCleanupMinutes;

        if (setupMinutes < 0 || cleanupMinutes < 0)
        {
            throw new ArgumentException("Setup and cleanup buffers cannot be negative.");
        }

        var occupiedStartUtc = eventStartUtc.AddMinutes(-setupMinutes);
        var occupiedEndUtc = eventEndUtc.AddMinutes(cleanupMinutes);

        // 1. Max booking duration check
        var eventDuration = eventEndUtc - eventStartUtc;
        if (eventDuration > TimeSpan.FromHours(venue.MaxBookingHours))
        {
            return new CheckAvailabilityResponse(
                venue.Id,
                venue.Name,
                venue.Code,
                AvailabilityStatus.Unavailable,
                $"Requested event duration ({eventDuration.TotalHours:0.#} hours) exceeds the maximum booking limit of {venue.MaxBookingHours} hours for this venue.",
                false,
                eventStartUtc,
                eventEndUtc,
                occupiedStartUtc,
                occupiedEndUtc,
                setupMinutes,
                cleanupMinutes,
                null,
                null
            );
        }

        // 2. Minimum advance notice check
        if (venue.MinNoticeHours > 0)
        {
            var earliestAllowedStart = DateTime.UtcNow.AddHours(venue.MinNoticeHours);
            if (eventStartUtc < earliestAllowedStart)
            {
                return new CheckAvailabilityResponse(
                    venue.Id,
                    venue.Name,
                    venue.Code,
                    AvailabilityStatus.Unavailable,
                    $"Requested start time does not meet the minimum advance notice of {venue.MinNoticeHours} hours for this venue.",
                    false,
                    eventStartUtc,
                    eventEndUtc,
                    occupiedStartUtc,
                    occupiedEndUtc,
                    setupMinutes,
                    cleanupMinutes,
                    null,
                    null
                );
            }
        }

        // 3. Operating hours check in local university time (UTC+05:30)
        var localOccupiedStart = new DateTimeOffset(occupiedStartUtc, TimeSpan.Zero).ToOffset(SriLankaOffset);
        var localOccupiedEnd = new DateTimeOffset(occupiedEndUtc, TimeSpan.Zero).ToOffset(SriLankaOffset);

        if (localOccupiedStart.Date != localOccupiedEnd.Date)
        {
            return new CheckAvailabilityResponse(
                venue.Id,
                venue.Name,
                venue.Code,
                AvailabilityStatus.Unavailable,
                "The requested occupied window spans multiple calendar days in local university time, which is not permitted.",
                false,
                eventStartUtc,
                eventEndUtc,
                occupiedStartUtc,
                occupiedEndUtc,
                setupMinutes,
                cleanupMinutes,
                null,
                null
            );
        }

        var localStartTime = TimeOnly.FromTimeSpan(localOccupiedStart.TimeOfDay);
        var localEndTime = TimeOnly.FromTimeSpan(localOccupiedEnd.TimeOfDay);

        if (localStartTime < venue.OperatingHoursStart || localEndTime > venue.OperatingHoursEnd)
        {
            return new CheckAvailabilityResponse(
                venue.Id,
                venue.Name,
                venue.Code,
                AvailabilityStatus.Unavailable,
                $"The occupied interval ({localStartTime:HH:mm} - {localEndTime:HH:mm} local time, including buffers) falls outside venue operating hours ({venue.OperatingHoursStart:HH:mm} - {venue.OperatingHoursEnd:HH:mm}).",
                false,
                eventStartUtc,
                eventEndUtc,
                occupiedStartUtc,
                occupiedEndUtc,
                setupMinutes,
                cleanupMinutes,
                null,
                null
            );
        }

        // 4. Calendar Coverage Check: Ensure verified coverage encompasses [occupiedStartUtc, occupiedEndUtc]
        var coverage = await dbContext.VenueCalendarCoverages
            .Include(c => c.VerifiedByUser)
            .Where(c => c.VenueId == venue.Id &&
                        c.CoversFromUtc <= occupiedStartUtc &&
                        c.CoversToUtc >= occupiedEndUtc)
            .OrderByDescending(c => c.VerifiedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (coverage == null)
        {
            var latestCoverage = await dbContext.VenueCalendarCoverages
                .Where(c => c.VenueId == venue.Id)
                .OrderByDescending(c => c.CoversToUtc)
                .FirstOrDefaultAsync(cancellationToken);

            string unverifiedReason = latestCoverage == null
                ? "Availability unverified: No verified calendar coverage exists for this venue. Facility staff have not confirmed external timetable or booking sources."
                : $"Availability unverified: The requested window ({occupiedStartUtc:yyyy-MM-dd HH:mm} to {occupiedEndUtc:yyyy-MM-dd HH:mm} UTC) falls outside verified calendar coverage (latest verified period ended {latestCoverage.CoversToUtc:yyyy-MM-dd}).";

            return new CheckAvailabilityResponse(
                venue.Id,
                venue.Name,
                venue.Code,
                AvailabilityStatus.Unverified,
                unverifiedReason,
                false,
                eventStartUtc,
                eventEndUtc,
                occupiedStartUtc,
                occupiedEndUtc,
                setupMinutes,
                cleanupMinutes,
                null,
                null
            );
        }

        var coverageDto = new CoverageDetailsDto(
            coverage.SourceName,
            coverage.SourceReference,
            coverage.CoversFromUtc,
            coverage.CoversToUtc,
            coverage.VerifiedByUser.Email ?? string.Empty,
            coverage.VerifiedAtUtc
        );

        // 5. Calendar Blocks Overlap Check
        var conflictingBlock = await dbContext.VenueCalendarBlocks
            .Where(b => b.VenueId == venue.Id &&
                        b.StartUtc < occupiedEndUtc &&
                        b.EndUtc > occupiedStartUtc)
            .OrderBy(b => b.StartUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (conflictingBlock != null)
        {
            bool directEventOverlap = conflictingBlock.StartUtc < eventEndUtc && conflictingBlock.EndUtc > eventStartUtc;
            bool isBufferOnly = !directEventOverlap;
            string conflictScope;
            string reason;

            if (directEventOverlap)
            {
                conflictScope = "Event";
                reason = $"Conflict detected: Venue is occupied by '{conflictingBlock.Title}' ({conflictingBlock.BlockType}) from {conflictingBlock.StartUtc:yyyy-MM-dd HH:mm} to {conflictingBlock.EndUtc:yyyy-MM-dd HH:mm} UTC.";
            }
            else if (conflictingBlock.EndUtc > occupiedStartUtc && conflictingBlock.StartUtc < eventStartUtc)
            {
                conflictScope = "SetupBuffer";
                reason = $"Conflict detected with setup buffer: Existing block '{conflictingBlock.Title}' ({conflictingBlock.BlockType}, {conflictingBlock.StartUtc:yyyy-MM-dd HH:mm} to {conflictingBlock.EndUtc:yyyy-MM-dd HH:mm} UTC) overlaps with the required {setupMinutes}-minute setup buffer ({occupiedStartUtc:HH:mm} - {eventStartUtc:HH:mm} UTC).";
            }
            else
            {
                conflictScope = "CleanupBuffer";
                reason = $"Conflict detected with cleanup buffer: Existing block '{conflictingBlock.Title}' ({conflictingBlock.BlockType}, {conflictingBlock.StartUtc:yyyy-MM-dd HH:mm} to {conflictingBlock.EndUtc:yyyy-MM-dd HH:mm} UTC) overlaps with the required {cleanupMinutes}-minute cleanup buffer ({eventEndUtc:HH:mm} - {occupiedEndUtc:HH:mm} UTC).";
            }

            var conflictDto = new ConflictDetailsDto(
                conflictingBlock.Id,
                conflictingBlock.Title,
                conflictingBlock.BlockType,
                conflictingBlock.StartUtc,
                conflictingBlock.EndUtc,
                isBufferOnly,
                conflictScope
            );

            return new CheckAvailabilityResponse(
                venue.Id,
                venue.Name,
                venue.Code,
                AvailabilityStatus.Unavailable,
                reason,
                false,
                eventStartUtc,
                eventEndUtc,
                occupiedStartUtc,
                occupiedEndUtc,
                setupMinutes,
                cleanupMinutes,
                conflictDto,
                coverageDto
            );
        }

        // 6. Slot is Verified & Available
        return new CheckAvailabilityResponse(
            venue.Id,
            venue.Name,
            venue.Code,
            AvailabilityStatus.Available,
            "Venue is available for the requested event window and required setup/cleanup buffers.",
            true,
            eventStartUtc,
            eventEndUtc,
            occupiedStartUtc,
            occupiedEndUtc,
            setupMinutes,
            cleanupMinutes,
            null,
            coverageDto
        );
    }
}
