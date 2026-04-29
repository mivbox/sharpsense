using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpSense.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceTreeNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkspaceTreeNodes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ParentId = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    HasChildren = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChildCount = table.Column<int>(type: "INTEGER", nullable: true),
                    IsSelectable = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceTreeNodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceTreeNodes_ParentId",
                table: "WorkspaceTreeNodes",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceTreeNodes_Path",
                table: "WorkspaceTreeNodes",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceTreeNodes_ProjectId",
                table: "WorkspaceTreeNodes",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceTreeNodes");
        }
    }
}
