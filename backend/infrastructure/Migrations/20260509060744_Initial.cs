using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reviews.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reviews");

            migrationBuilder.CreateTable(
                name: "products",
                schema: "reviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                schema: "reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorName = table.Column<string>(type: "text", nullable: false),
                    Rating = table.Column<short>(type: "smallint", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ImageUrls = table.Column<List<string>>(type: "text[]", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Score = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reviews", x => x.Id);
                    table.CheckConstraint("ck_reviews_rating", "\"Rating\" BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_reviews_status", "\"Status\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_reviews_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "reviews",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_votes",
                schema: "reviews",
                columns: table => new
                {
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    VoterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_votes", x => new { x.ReviewId, x.VoterId });
                    table.CheckConstraint("ck_review_votes_value", "\"Value\" IN (-1, 1)");
                    table.ForeignKey(
                        name: "FK_review_votes_reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalSchema: "reviews",
                        principalTable: "reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_products_Slug",
                schema: "reviews",
                table: "products",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_reviews_helpful",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "ProductId", "Score", "Id" },
                descending: new[] { false, true, true },
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "idx_reviews_newest",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "ProductId", "CreatedAt", "Id" },
                descending: new[] { false, true, true },
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "idx_reviews_rating",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "ProductId", "Rating", "CreatedAt", "Id" },
                descending: new[] { false, true, true, true },
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "uq_reviews_product_author",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "ProductId", "AuthorId" },
                unique: true,
                filter: "\"Status\" <> 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_votes",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "reviews",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "products",
                schema: "reviews");
        }
    }
}
