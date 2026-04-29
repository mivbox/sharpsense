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
                name: "Directories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Directories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Directories_Directories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Directories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GraphNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CanonicalId = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GraphNodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DirectoryClosures",
                columns: table => new
                {
                    AncestorDirectoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    DescendantDirectoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectoryClosures", x => new { x.AncestorDirectoryId, x.DescendantDirectoryId });
                    table.CheckConstraint("CK_DirectoryClosures_Depth", "Depth >= 0");
                    table.ForeignKey(
                        name: "FK_DirectoryClosures_Directories_AncestorDirectoryId",
                        column: x => x.AncestorDirectoryId,
                        principalTable: "Directories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DirectoryClosures_Directories_DescendantDirectoryId",
                        column: x => x.DescendantDirectoryId,
                        principalTable: "Directories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DirectoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_Directories_DirectoryId",
                        column: x => x.DirectoryId,
                        principalTable: "Directories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DependencyEdges",
                columns: table => new
                {
                    CallerNodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    CalleeNodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    EdgeType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DependencyEdges", x => new { x.CallerNodeId, x.CalleeNodeId, x.EdgeType });
                    table.ForeignKey(
                        name: "FK_DependencyEdges_GraphNodes_CalleeNodeId",
                        column: x => x.CalleeNodeId,
                        principalTable: "GraphNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DependencyEdges_GraphNodes_CallerNodeId",
                        column: x => x.CallerNodeId,
                        principalTable: "GraphNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ProjectDocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectNodes_Documents_ProjectDocumentId",
                        column: x => x.ProjectDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectNodes_GraphNodes_Id",
                        column: x => x.Id,
                        principalTable: "GraphNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CodeNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    ProjectNodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    FullyQualifiedName = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    NodeType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StartLine = table.Column<int>(type: "INTEGER", nullable: false),
                    EndLine = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    VectorEmbedding = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeNodes_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CodeNodes_GraphNodes_Id",
                        column: x => x.Id,
                        principalTable: "GraphNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CodeNodes_ProjectNodes_ProjectNodeId",
                        column: x => x.ProjectNodeId,
                        principalTable: "ProjectNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodeNodes_DocumentId",
                table: "CodeNodes",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeNodes_FullyQualifiedName",
                table: "CodeNodes",
                column: "FullyQualifiedName");

            migrationBuilder.CreateIndex(
                name: "IX_CodeNodes_ProjectNodeId",
                table: "CodeNodes",
                column: "ProjectNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_DependencyEdges_CalleeNodeId",
                table: "DependencyEdges",
                column: "CalleeNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_DependencyEdges_CallerNodeId",
                table: "DependencyEdges",
                column: "CallerNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Directories_ParentId",
                table: "Directories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Directories_ParentId_Name",
                table: "Directories",
                columns: new[] { "ParentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Directories_Path",
                table: "Directories",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryClosures_DescendantDirectoryId_AncestorDirectoryId",
                table: "DirectoryClosures",
                columns: new[] { "DescendantDirectoryId", "AncestorDirectoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DirectoryId",
                table: "Documents",
                column: "DirectoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DirectoryId_FileName",
                table: "Documents",
                columns: new[] { "DirectoryId", "FileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_RelativePath",
                table: "Documents",
                column: "RelativePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GraphNodes_CanonicalId",
                table: "GraphNodes",
                column: "CanonicalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GraphNodes_Kind",
                table: "GraphNodes",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectNodes_ProjectDocumentId",
                table: "ProjectNodes",
                column: "ProjectDocumentId",
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS IX_CodeNodes_FullyQualifiedName_NoCase
                ON CodeNodes(FullyQualifiedName COLLATE NOCASE);
                """);
            migrationBuilder.Sql(
                """
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
                name: "DirectoryClosures");

            migrationBuilder.DropTable(
                name: "ProjectNodes");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "GraphNodes");

            migrationBuilder.DropTable(
                name: "Directories");
        }
    }
}
