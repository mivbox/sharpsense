using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpSense.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MemoryLayerCascadeAndUniqueFqdn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CodeNodes_FullyQualifiedName",
                table: "CodeNodes");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CodeNodes_FullyQualifiedName",
                table: "CodeNodes",
                column: "FullyQualifiedName");

            migrationBuilder.CreateIndex(
                name: "IX_CodeNodes_FullyQualifiedName",
                table: "CodeNodes",
                column: "FullyQualifiedName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MemoryNodes_CodeNodes_TargetFullyQualifiedName",
                table: "MemoryNodes",
                column: "TargetFullyQualifiedName",
                principalTable: "CodeNodes",
                principalColumn: "FullyQualifiedName",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MemoryNodes_CodeNodes_TargetFullyQualifiedName",
                table: "MemoryNodes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CodeNodes_FullyQualifiedName",
                table: "CodeNodes");

            migrationBuilder.DropIndex(
                name: "IX_CodeNodes_FullyQualifiedName",
                table: "CodeNodes");

            migrationBuilder.CreateIndex(
                name: "IX_CodeNodes_FullyQualifiedName",
                table: "CodeNodes",
                column: "FullyQualifiedName");
        }
    }
}
