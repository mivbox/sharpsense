using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpSense.Infrastructure.Persistence.Migrations;

public partial class IndexRunState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "IndexRunState",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                LastSuccessfulIndexJson = table.Column<string>(type: "TEXT", nullable: true),
                LastAttemptJson = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IndexRunState", x => x.Id);
                table.CheckConstraint("CK_IndexRunState_Singleton", "Id = 1");
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "IndexRunState");
    }
}
