using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoNest.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusAndFilePathToVideo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FilePath",
                table: "Videos",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Videos",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilePath",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Videos");
        }
    }
}
