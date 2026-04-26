using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpSense.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CodeNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CanonicalId = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    FullyQualifiedName = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    NodeType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RelativeFilePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    StartLine = table.Column<int>(type: "INTEGER", nullable: false),
                    EndLine = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    VectorEmbedding = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeNodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DependencyEdges",
                columns: table => new
                {
                    CallerId = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    CalleeId = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    EdgeType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DependencyEdges", x => new { x.CallerId, x.CalleeId, x.EdgeType });
                });

            migrationBuilder.CreateTable(
                name: "ProjectNodes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    RelativeFilePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectNodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodeNodes_CanonicalId",
                table: "CodeNodes",
                column: "CanonicalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodeNodes_FullyQualifiedName",
                table: "CodeNodes",
                column: "FullyQualifiedName");

            migrationBuilder.CreateIndex(
                name: "IX_CodeNodes_ProjectId",
                table: "CodeNodes",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeNodes_RelativeFilePath",
                table: "CodeNodes",
                column: "RelativeFilePath");

            migrationBuilder.CreateIndex(
                name: "IX_DependencyEdges_CalleeId",
                table: "DependencyEdges",
                column: "CalleeId");

            migrationBuilder.CreateIndex(
                name: "IX_DependencyEdges_CallerId",
                table: "DependencyEdges",
                column: "CallerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectNodes_RelativeFilePath",
                table: "ProjectNodes",
                column: "RelativeFilePath",
                unique: true);

            migrationBuilder.Sql("""
                                 CREATE INDEX IF NOT EXISTS IX_CodeNodes_FullyQualifiedName_NoCase
                                 ON CodeNodes(FullyQualifiedName COLLATE NOCASE);
                                 """);

            migrationBuilder.Sql("""
                                 CREATE VIRTUAL TABLE IF NOT EXISTS CodeNodeSearch
                                 USING fts5(
                                     Id UNINDEXED,
                                     CanonicalId UNINDEXED,
                                     DisplayName,
                                     FullyQualifiedName,
                                     Summary,
                                     RelativeFilePath,
                                     tokenize = 'unicode61'
                                 );
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS CodeNodeSearch;");

            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_CodeNodes_FullyQualifiedName_NoCase;");

            migrationBuilder.DropTable(
                name: "CodeNodes");

            migrationBuilder.DropTable(
                name: "DependencyEdges");

            migrationBuilder.DropTable(
                name: "ProjectNodes");
        }
    }
}
