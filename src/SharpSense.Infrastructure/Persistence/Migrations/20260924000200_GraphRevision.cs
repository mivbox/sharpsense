using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpSense.Infrastructure.Persistence.Migrations;

public partial class GraphRevision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(
            name: "GraphRevision",
            table: "IndexRunState",
            type: "TEXT",
            nullable: false,
            defaultValue: "initial");

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn("GraphRevision", "IndexRunState");
}
