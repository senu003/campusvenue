using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace CampusVenue.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmedReservationsAndExclusionConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "confirmed_reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    event_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    event_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    setup_minutes = table.Column<int>(type: "integer", nullable: false),
                    cleanup_minutes = table.Column<int>(type: "integer", nullable: false),
                    occupied_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    occupied_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    occupied_range = table.Column<NpgsqlRange<DateTime>>(type: "tstzrange", nullable: false),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_confirmed_reservations", x => x.id);
                    table.CheckConstraint("ck_confirmed_reservations_buffers", "setup_minutes >= 0 AND cleanup_minutes >= 0");
                    table.CheckConstraint("ck_confirmed_reservations_dates", "event_end_utc > event_start_utc");
                    table.CheckConstraint("ck_confirmed_reservations_occupied_dates", "occupied_end_utc > occupied_start_utc");
                    table.ForeignKey(
                        name: "fk_confirmed_reservations_users_confirmed_by_user_id",
                        column: x => x.confirmed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_confirmed_reservations_venue_booking_requests_booking_request_id",
                        column: x => x.booking_request_id,
                        principalTable: "venue_booking_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_confirmed_reservations_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_confirmed_reservations_booking_request_id",
                table: "confirmed_reservations",
                column: "booking_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_reservations_confirmed_by_user_id",
                table: "confirmed_reservations",
                column: "confirmed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_confirmed_reservations_reservation_reference",
                table: "confirmed_reservations",
                column: "reservation_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_confirmed_reservations_venue_id",
                table: "confirmed_reservations",
                column: "venue_id");

            migrationBuilder.CreateIndex(
                name: "ix_confirmed_reservations_venue_occupied_dates",
                table: "confirmed_reservations",
                columns: new[] { "venue_id", "occupied_start_utc", "occupied_end_utc" });

            // Ensure btree_gist extension exists for scalar + GiST range exclusion constraints
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            // PostgreSQL GiST temporal exclusion constraint preventing overlapping occupied ranges for the same venue
            migrationBuilder.Sql(@"
                ALTER TABLE confirmed_reservations
                ADD CONSTRAINT ex_confirmed_reservations_venue_occupied_range
                EXCLUDE USING gist (
                    venue_id WITH =,
                    occupied_range WITH &&
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE confirmed_reservations DROP CONSTRAINT IF EXISTS ex_confirmed_reservations_venue_occupied_range;");

            migrationBuilder.DropTable(
                name: "confirmed_reservations");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,");
        }
    }
}
