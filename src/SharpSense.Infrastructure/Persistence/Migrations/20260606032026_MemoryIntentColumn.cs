using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpSense.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MemoryIntentColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Intent",
                table: "MemoryNodes",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Convention");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Intent",
                table: "MemoryNodes");
        }
    }
}
