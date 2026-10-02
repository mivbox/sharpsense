using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpSense.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiTargetProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectNodes_ProjectDocumentId",
                table: "ProjectNodes");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectNodes_ProjectDocumentId",
                table: "ProjectNodes",
                column: "ProjectDocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectNodes_ProjectDocumentId",
                table: "ProjectNodes");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectNodes_ProjectDocumentId",
                table: "ProjectNodes",
                column: "ProjectDocumentId",
                unique: true);
        }
    }
}
