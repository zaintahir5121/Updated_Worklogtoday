using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorklogToday.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceNoteToNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudioUrl",
                table: "Notes",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Transcript",
                table: "Notes",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioUrl",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "Transcript",
                table: "Notes");
        }
    }
}
