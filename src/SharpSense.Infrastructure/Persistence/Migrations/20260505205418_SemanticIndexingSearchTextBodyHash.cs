using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpSense.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SemanticIndexingSearchTextBodyHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BodyHash",
                table: "CodeNodes",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "CodeNodes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE CodeNodes
                SET SearchText = CASE
                    WHEN trim(Summary) = '' THEN DisplayName
                    ELSE DisplayName || char(10) || Summary
                END;
                """);
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS CodeNodeSearch;
                CREATE VIRTUAL TABLE IF NOT EXISTS CodeNodeSearch
                USING fts5(
                    Id UNINDEXED,
                    CanonicalId UNINDEXED,
                    DisplayName,
                    FullyQualifiedName,
                    SearchText,
                    RelativeFilePath,
                    tokenize = 'unicode61'
                );
                INSERT INTO CodeNodeSearch (Id, CanonicalId, DisplayName, FullyQualifiedName, SearchText, RelativeFilePath)
                SELECT CodeNodes.Id, GraphNodes.CanonicalId, CodeNodes.DisplayName, CodeNodes.FullyQualifiedName, CodeNodes.SearchText, Documents.RelativePath
                FROM CodeNodes
                INNER JOIN GraphNodes ON GraphNodes.Id = CodeNodes.Id
                INNER JOIN Documents ON Documents.Id = CodeNodes.DocumentId;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS CodeNodeSearch;
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
                INSERT INTO CodeNodeSearch (Id, CanonicalId, DisplayName, FullyQualifiedName, Summary, RelativeFilePath)
                SELECT CodeNodes.Id, GraphNodes.CanonicalId, CodeNodes.DisplayName, CodeNodes.FullyQualifiedName, CodeNodes.Summary, Documents.RelativePath
                FROM CodeNodes
                INNER JOIN GraphNodes ON GraphNodes.Id = CodeNodes.Id
                INNER JOIN Documents ON Documents.Id = CodeNodes.DocumentId;
                """);
            migrationBuilder.DropColumn(
                name: "BodyHash",
                table: "CodeNodes");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "CodeNodes");
        }
    }
}
