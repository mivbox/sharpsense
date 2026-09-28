using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpSense.Infrastructure.Persistence.Migrations;

public partial class MemoryStableNodeIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Resolve every existing memory before dropping its original table. A missing target
        // produces NULL, violating NOT NULL and rolling back instead of discarding authored data.
        migrationBuilder.Sql("""
            CREATE TABLE "__MemoryNodes_StableIdentity" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_MemoryNodes" PRIMARY KEY,
                "TargetCodeNodeId" INTEGER NOT NULL,
                "TargetCodeHash" TEXT NOT NULL,
                "Content" TEXT NOT NULL,
                "ContentHash" TEXT NOT NULL,
                "TagsJson" TEXT NOT NULL,
                "Intent" TEXT NOT NULL,
                "VectorEmbedding" BLOB NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_MemoryNodes_CodeNodes_TargetCodeNodeId"
                    FOREIGN KEY ("TargetCodeNodeId") REFERENCES "CodeNodes" ("Id") ON DELETE CASCADE);
            INSERT INTO "__MemoryNodes_StableIdentity"
                ("Id", "TargetCodeNodeId", "TargetCodeHash", "Content", "ContentHash",
                 "TagsJson", "Intent", "VectorEmbedding", "CreatedAt")
            SELECT m."Id",
                (SELECT c."Id" FROM "CodeNodes" c WHERE c."FullyQualifiedName" = m."TargetFullyQualifiedName"),
                m."TargetCodeHash", m."Content", m."ContentHash", m."TagsJson",
                m."Intent", m."VectorEmbedding", m."CreatedAt"
            FROM "MemoryNodes" m;
            DROP TABLE "MemoryNodes";
            ALTER TABLE "__MemoryNodes_StableIdentity" RENAME TO "MemoryNodes";
            CREATE INDEX "IX_MemoryNodes_TargetCodeNodeId" ON "MemoryNodes" ("TargetCodeNodeId");
            CREATE INDEX "IX_MemoryNodes_ContentHash" ON "MemoryNodes" ("ContentHash");
            CREATE INDEX "IX_MemoryNodes_CreatedAt" ON "MemoryNodes" ("CreatedAt");
            """);

        // FullyQualifiedName remains uniquely indexed, but is no longer an immutable EF key.
        migrationBuilder.DropUniqueConstraint(
            name: "AK_CodeNodes_FullyQualifiedName",
            table: "CodeNodes");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "__MemoryNodes_NamedIdentity" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_MemoryNodes" PRIMARY KEY,
                "TargetFullyQualifiedName" TEXT NOT NULL,
                "TargetCodeHash" TEXT NOT NULL,
                "Content" TEXT NOT NULL,
                "ContentHash" TEXT NOT NULL,
                "TagsJson" TEXT NOT NULL,
                "Intent" TEXT NOT NULL,
                "VectorEmbedding" BLOB NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_MemoryNodes_CodeNodes_TargetFullyQualifiedName"
                    FOREIGN KEY ("TargetFullyQualifiedName") REFERENCES "CodeNodes" ("FullyQualifiedName") ON DELETE CASCADE);
            INSERT INTO "__MemoryNodes_NamedIdentity"
                ("Id", "TargetFullyQualifiedName", "TargetCodeHash", "Content", "ContentHash",
                 "TagsJson", "Intent", "VectorEmbedding", "CreatedAt")
            SELECT m."Id",
                (SELECT c."FullyQualifiedName" FROM "CodeNodes" c WHERE c."Id" = m."TargetCodeNodeId"),
                m."TargetCodeHash", m."Content", m."ContentHash", m."TagsJson",
                m."Intent", m."VectorEmbedding", m."CreatedAt"
            FROM "MemoryNodes" m;
            DROP TABLE "MemoryNodes";
            ALTER TABLE "__MemoryNodes_NamedIdentity" RENAME TO "MemoryNodes";
            CREATE INDEX "IX_MemoryNodes_TargetFullyQualifiedName" ON "MemoryNodes" ("TargetFullyQualifiedName");
            CREATE INDEX "IX_MemoryNodes_ContentHash" ON "MemoryNodes" ("ContentHash");
            CREATE INDEX "IX_MemoryNodes_CreatedAt" ON "MemoryNodes" ("CreatedAt");
            """);

        migrationBuilder.AddUniqueConstraint(
            name: "AK_CodeNodes_FullyQualifiedName",
            table: "CodeNodes",
            column: "FullyQualifiedName");
    }
}
