using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpSense.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GraphNativeMemoryLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MemoryNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetFullyQualifiedName = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    TargetCodeHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TagsJson = table.Column<string>(type: "TEXT", nullable: false),
                    VectorEmbedding = table.Column<byte[]>(type: "BLOB", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryNodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryNodes_ContentHash",
                table: "MemoryNodes",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_MemoryNodes_CreatedAt",
                table: "MemoryNodes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MemoryNodes_TargetFullyQualifiedName",
                table: "MemoryNodes",
                column: "TargetFullyQualifiedName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemoryNodes");
        }
    }
}
