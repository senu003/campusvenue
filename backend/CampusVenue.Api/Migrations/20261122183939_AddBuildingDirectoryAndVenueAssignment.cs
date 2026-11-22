using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusVenue.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingDirectoryAndVenueAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "building_id",
                table: "venues",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "buildings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_buildings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_venues_building_id",
                table: "venues",
                column: "building_id");

            migrationBuilder.CreateIndex(
                name: "ix_buildings_code",
                table: "buildings",
                column: "code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_venues_buildings_building_id",
                table: "venues",
                column: "building_id",
                principalTable: "buildings",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_venues_buildings_building_id",
                table: "venues");

            migrationBuilder.DropTable(
                name: "buildings");

            migrationBuilder.DropIndex(
                name: "ix_venues_building_id",
                table: "venues");

            migrationBuilder.DropColumn(
                name: "building_id",
                table: "venues");
        }
    }
}
