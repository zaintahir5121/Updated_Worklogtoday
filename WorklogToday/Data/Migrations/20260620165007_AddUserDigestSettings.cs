using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorklogToday.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDigestSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailDigestEnabled",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "HourlyRate",
                table: "AspNetUsers",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailDigestEnabled",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HourlyRate",
                table: "AspNetUsers");
        }
    }
}
