using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusVenue.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingRequestsAndVenueReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "general_rules",
                table: "venues",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "venue_booking_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    expected_attendance = table.Column<int>(type: "integer", nullable: false),
                    event_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    event_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    setup_minutes = table.Column<int>(type: "integer", nullable: false),
                    cleanup_minutes = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    review_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    conditions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    conditions_accepted = table.Column<bool>(type: "boolean", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_venue_booking_requests", x => x.id);
                    table.CheckConstraint("ck_venue_booking_requests_attendance", "expected_attendance > 0");
                    table.CheckConstraint("ck_venue_booking_requests_buffers", "setup_minutes >= 0 AND cleanup_minutes >= 0");
                    table.CheckConstraint("ck_venue_booking_requests_dates", "event_end_utc > event_start_utc");
                    table.ForeignKey(
                        name: "fk_venue_booking_requests_users_requester_id",
                        column: x => x.requester_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_venue_booking_requests_users_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_venue_booking_requests_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "booking_notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    notification_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body_html = table.Column<string>(type: "text", nullable: false),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_notifications_users_recipient_user_id",
                        column: x => x.recipient_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_booking_notifications_venue_booking_requests_booking_request_id",
                        column: x => x.booking_request_id,
                        principalTable: "venue_booking_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_booking_notifications_booking_request_id",
                table: "booking_notifications",
                column: "booking_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_booking_notifications_recipient_user_id",
                table: "booking_notifications",
                column: "recipient_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_venue_booking_requests_idempotency_key",
                table: "venue_booking_requests",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_venue_booking_requests_requester_id",
                table: "venue_booking_requests",
                column: "requester_id");

            migrationBuilder.CreateIndex(
                name: "IX_venue_booking_requests_reviewed_by_user_id",
                table: "venue_booking_requests",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_venue_booking_requests_status",
                table: "venue_booking_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_venue_booking_requests_venue_dates",
                table: "venue_booking_requests",
                columns: new[] { "venue_id", "event_start_utc", "event_end_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_venue_booking_requests_venue_id",
                table: "venue_booking_requests",
                column: "venue_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_notifications");

            migrationBuilder.DropTable(
                name: "venue_booking_requests");

            migrationBuilder.DropColumn(
                name: "general_rules",
                table: "venues");
        }
    }
}
