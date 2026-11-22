using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Modules.Facilities.Domain;
using CampusVenue.Api.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CampusVenue.Api.Modules.Facilities.Services;

/// <summary>
/// Ensures the local pilot demo venues exist in PostgreSQL.
/// Concurrency-safe for multi-instance or parallel test host startup.
/// </summary>
public static class VenueSeeder
{
    private static readonly SemaphoreSlim SeederLock = new(1, 1);

    public static async Task SeedVenuesAsync(IServiceProvider services, ILogger? logger = null)
    {
        await SeederLock.WaitAsync();
        try
        {
            using var scope = services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var adminUser = await userManager.Users
                .OrderBy(u => u.CreatedAtUtc)
                .FirstOrDefaultAsync();

            if (adminUser == null)
            {
                return;
            }

            // 1. Central Hall: Civil Auditorium (J.G. Hall)
            if (!await dbContext.Venues.AnyAsync(v => v.Code == "JG-HALL"))
            {
                var jgHall = new Venue
                {
                    Id = Guid.NewGuid(),
                    Name = "Civil Auditorium (J.G. Hall)",
                    Code = "JG-HALL",
                    Type = "Central",
                    ManagingUnit = "General Administration / Student Welfare",
                    Capacity = 600,
                    OperatingHoursStart = new TimeOnly(8, 0),
                    OperatingHoursEnd = new TimeOnly(20, 0),
                    MinNoticeHours = 72,
                    DefaultSetupMinutes = 45,
                    DefaultCleanupMinutes = 30,
                    MaxBookingHours = 8,
                    RequiresEventApproval = true,
                    GeneralRules = "1. No food or open drink containers allowed inside the auditorium.\n2. Audio-visual equipment requires prior briefing with technical staff.\n3. Noise restrictions apply after 20:00.\n4. Clean and restore seating layout following event conclusion.",
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                dbContext.Venues.Add(jgHall);

                var coverage = new VenueCalendarCoverage
                {
                    Id = Guid.NewGuid(),
                    VenueId = jgHall.Id,
                    SourceName = "CITeS RBS Staff System (book.uom.lk)",
                    SourceReference = "Semester 2 2026 Academic Term Master Schedule",
                    CoversFromUtc = DateTime.UtcNow.Date.AddDays(-14),
                    CoversToUtc = DateTime.UtcNow.Date.AddDays(90),
                    VerifiedByUserId = adminUser.Id,
                    VerifiedAtUtc = DateTime.UtcNow,
                    Notes = "Verified against published RBS reservations and Student Welfare central exam calendar.",
                    CreatedAtUtc = DateTime.UtcNow
                };
                dbContext.VenueCalendarCoverages.Add(coverage);

                var sampleExamStart = DateTime.UtcNow.Date.AddDays(7).AddHours(3).AddMinutes(30);
                var sampleExamEnd = sampleExamStart.AddHours(3);
                dbContext.VenueCalendarBlocks.Add(new VenueCalendarBlock
                {
                    Id = Guid.NewGuid(),
                    VenueId = jgHall.Id,
                    Title = "End-Semester Examinations: CE2010 Fluid Mechanics",
                    BlockType = "Exam",
                    StartUtc = sampleExamStart,
                    EndUtc = sampleExamEnd,
                    CreatedByUserId = adminUser.Id,
                    CreatedAtUtc = DateTime.UtcNow
                });

                try
                {
                    await dbContext.SaveChangesAsync();
                    logger?.LogInformation("Seeded central demo venue: {VenueName} ({VenueCode})", jgHall.Name, jgHall.Code);
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    logger?.LogInformation("Venue 'JG-HALL' was provisioned concurrently by another host.");
                }
            }

            // 2. Department Room: Faculty of Business Room 101
            if (!await dbContext.Venues.AnyAsync(v => v.Code == "FOB-101"))
            {
                var fobRoom = new Venue
                {
                    Id = Guid.NewGuid(),
                    Name = "Faculty of Business Room 101",
                    Code = "FOB-101",
                    Type = "Departmental",
                    ManagingUnit = "Faculty of Business / Undergraduate Studies",
                    Capacity = 80,
                    OperatingHoursStart = new TimeOnly(8, 0),
                    OperatingHoursEnd = new TimeOnly(18, 0),
                    MinNoticeHours = 24,
                    DefaultSetupMinutes = 15,
                    DefaultCleanupMinutes = 15,
                    MaxBookingHours = 4,
                    RequiresEventApproval = false,
                    GeneralRules = "1. Departmental lecture room rules apply.\n2. Return whiteboard markers and power down projectors upon departure.\n3. Keep corridors quiet during active class hours.",
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                dbContext.Venues.Add(fobRoom);

                var coverage = new VenueCalendarCoverage
                {
                    Id = Guid.NewGuid(),
                    VenueId = fobRoom.Id,
                    SourceName = "Faculty Undergraduate Timetable (uomba.net)",
                    SourceReference = "Term 2 2026 Master Timetable v1.2",
                    CoversFromUtc = DateTime.UtcNow.Date.AddDays(-14),
                    CoversToUtc = DateTime.UtcNow.Date.AddDays(60),
                    VerifiedByUserId = adminUser.Id,
                    VerifiedAtUtc = DateTime.UtcNow,
                    Notes = "Audited against uomba.net weekly departmental lecture allocations.",
                    CreatedAtUtc = DateTime.UtcNow
                };
                dbContext.VenueCalendarCoverages.Add(coverage);

                var sampleLectureStart = DateTime.UtcNow.Date.AddDays(5).AddHours(3);
                var sampleLectureEnd = sampleLectureStart.AddHours(2);
                dbContext.VenueCalendarBlocks.Add(new VenueCalendarBlock
                {
                    Id = Guid.NewGuid(),
                    VenueId = fobRoom.Id,
                    Title = "Lecture: BM2010 Financial Management",
                    BlockType = "Class",
                    StartUtc = sampleLectureStart,
                    EndUtc = sampleLectureEnd,
                    CreatedByUserId = adminUser.Id,
                    CreatedAtUtc = DateTime.UtcNow
                });

                try
                {
                    await dbContext.SaveChangesAsync();
                    logger?.LogInformation("Seeded department demo venue: {VenueName} ({VenueCode})", fobRoom.Name, fobRoom.Code);
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    logger?.LogInformation("Venue 'FOB-101' was provisioned concurrently by another host.");
                }
            }
        }
        finally
        {
            SeederLock.Release();
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pgEx)
        {
            return pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
        }
        return false;
    }
}
