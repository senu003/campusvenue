using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusVenue.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVenueCatalogAndVerifiedAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "venues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    managing_unit = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    operating_hours_start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    operating_hours_end = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    min_notice_hours = table.Column<int>(type: "integer", nullable: false),
                    default_setup_minutes = table.Column<int>(type: "integer", nullable: false),
                    default_cleanup_minutes = table.Column<int>(type: "integer", nullable: false),
                    max_booking_hours = table.Column<int>(type: "integer", nullable: false),
                    requires_event_approval = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_venues", x => x.id);
                    table.CheckConstraint("ck_venues_capacity_positive", "capacity > 0");
                    table.CheckConstraint("ck_venues_cleanup_buffer_nonnegative", "default_cleanup_minutes >= 0");
                    table.CheckConstraint("ck_venues_max_booking_positive", "max_booking_hours > 0");
                    table.CheckConstraint("ck_venues_min_notice_positive", "min_notice_hours >= 0");
                    table.CheckConstraint("ck_venues_setup_buffer_nonnegative", "default_setup_minutes >= 0");
                });

            migrationBuilder.CreateTable(
                name: "venue_calendar_blocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    block_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_venue_calendar_blocks", x => x.id);
                    table.CheckConstraint("ck_venue_calendar_blocks_dates", "end_utc > start_utc");
                    table.ForeignKey(
                        name: "fk_venue_calendar_blocks_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_venue_calendar_blocks_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "venue_calendar_coverages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    covers_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    covers_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    verified_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_venue_calendar_coverages", x => x.id);
                    table.CheckConstraint("ck_venue_calendar_coverages_dates", "covers_to_utc > covers_from_utc");
                    table.ForeignKey(
                        name: "fk_venue_calendar_coverages_users_verified_by_user_id",
                        column: x => x.verified_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_venue_calendar_coverages_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "venue_managers",
                columns: table => new
                {
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_venue_managers", x => new { x.venue_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_venue_managers_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_venue_managers_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_venue_calendar_blocks_created_by_user_id",
                table: "venue_calendar_blocks",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_venue_calendar_blocks_venue_id_dates",
                table: "venue_calendar_blocks",
                columns: new[] { "venue_id", "start_utc", "end_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_venue_calendar_coverages_venue_id_coverage",
                table: "venue_calendar_coverages",
                columns: new[] { "venue_id", "covers_from_utc", "covers_to_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_venue_calendar_coverages_verified_by_user_id",
                table: "venue_calendar_coverages",
                column: "verified_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_venue_managers_user_id",
                table: "venue_managers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_venues_code",
                table: "venues",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_venues_is_active",
                table: "venues",
                column: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "venue_calendar_blocks");

            migrationBuilder.DropTable(
                name: "venue_calendar_coverages");

            migrationBuilder.DropTable(
                name: "venue_managers");

            migrationBuilder.DropTable(
                name: "venues");
        }
    }
}
