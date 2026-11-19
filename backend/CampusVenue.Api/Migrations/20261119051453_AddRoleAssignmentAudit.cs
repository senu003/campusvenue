using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusVenue.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleAssignmentAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "role_assignment_audits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_roles = table.Column<List<string>>(type: "text[]", nullable: false),
                    new_roles = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_assignment_audits", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_assignment_audits_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_role_assignment_audits_users_target_user_id",
                        column: x => x.target_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_role_assignment_audits_actor_id",
                table: "role_assignment_audits",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_assignment_audits_created_at_utc",
                table: "role_assignment_audits",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_role_assignment_audits_target_user_id",
                table: "role_assignment_audits",
                column: "target_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_assignment_audits");
        }
    }
}
