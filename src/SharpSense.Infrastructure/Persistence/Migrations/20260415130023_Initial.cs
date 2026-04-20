using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpSense.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        public const int DefaultVectorDimensions = 384;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CodeNodes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    FullyQualifiedName = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    NodeType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RelativeFilePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
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

            // Create FTS5 Virtual Table
            migrationBuilder.Sql(@"
                CREATE VIRTUAL TABLE IF NOT EXISTS CodeNodesSearch
                USING fts5(Id UNINDEXED, FullyQualifiedName, Summary);
            ");

            // Create vec0 Virtual Table (Assuming DefaultVectorDimensions is 384 for bge-micro-v2)
            migrationBuilder.Sql($@"
                CREATE VIRTUAL TABLE IF NOT EXISTS CodeNodesVectors
                USING vec0(Id TEXT PARTITION KEY, VectorEmbedding float[{DefaultVectorDimensions}]);
            ");

            migrationBuilder.Sql("""
                                 CREATE VIRTUAL TABLE IF NOT EXISTS CodeNodeSearch
                                 USING fts5(
                                     Id UNINDEXED,
                                     FullyQualifiedName,
                                     Summary,
                                     RelativeFilePath,
                                     tokenize = 'unicode61'
                                 );
                                 """);

            migrationBuilder.Sql($"""
                                  CREATE VIRTUAL TABLE IF NOT EXISTS CodeNodeVectors
                                  USING vec0(
                                      CodeNodeId TEXT,
                                      ProjectId TEXT,
                                      Embedding FLOAT[{DefaultVectorDimensions}] distance_metric=cosine
                                  );
                                  """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS CodeNodesVectors;");

            migrationBuilder.Sql("DROP TABLE IF EXISTS CodeNodesSearch;");

            migrationBuilder.DropTable(
                name: "CodeNodes");

            migrationBuilder.DropTable(
                name: "DependencyEdges");

            migrationBuilder.DropTable(
                name: "ProjectNodes");
        }
    }
}
