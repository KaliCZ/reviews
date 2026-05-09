using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reviews.Infrastructure.Migrations
{
    [DbContext(typeof(ReviewsDbContext))]
    [Migration("20260509100000_AddReviewLanguage")]
    public partial class AddReviewLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default 'en' on existing rows so the NOT NULL constraint
            // applies cleanly. New writes go through the entity ctor which
            // now requires `language` explicitly — no implicit-default
            // surprise; the default is a backfill aid, not a runtime path.
            migrationBuilder.AddColumn<string>(
                name: "Language",
                schema: "reviews",
                table: "reviews",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "en");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                schema: "reviews",
                table: "reviews");
        }
    }
}
