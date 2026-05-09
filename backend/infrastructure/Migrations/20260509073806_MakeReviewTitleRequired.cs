using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reviews.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeReviewTitleRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill any pre-existing NULL titles with a non-empty placeholder
            // so the NOT NULL alter doesn't break and the new NonEmptyString
            // contract holds for hydrated rows. Empty-string would persist a
            // value that the entity ctor would refuse to round-trip.
            migrationBuilder.Sql(
                "UPDATE reviews.reviews SET \"Title\" = '(no title)' WHERE \"Title\" IS NULL");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                schema: "reviews",
                table: "reviews",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Title",
                schema: "reviews",
                table: "reviews",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }
    }
}
