using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpSense.Infrastructure.Persistence.Migrations;

public partial class ProjectQualifiedSymbols : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_CodeNodes_FullyQualifiedName", "CodeNodes");
        migrationBuilder.CreateIndex(
            name: "IX_CodeNodes_FullyQualifiedName",
            table: "CodeNodes",
            column: "FullyQualifiedName");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_CodeNodes_FullyQualifiedName", "CodeNodes");
        migrationBuilder.CreateIndex(
            name: "IX_CodeNodes_FullyQualifiedName",
            table: "CodeNodes",
            column: "FullyQualifiedName",
            unique: true);
    }
}
