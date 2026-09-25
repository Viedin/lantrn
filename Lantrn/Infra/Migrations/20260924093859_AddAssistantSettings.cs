using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lantrn.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Assistant_ApiKey",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Assistant_BaseUrl",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Assistant_Enabled",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Assistant_MaxSources",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Assistant_Model",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Assistant_TimeoutSeconds",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Assistant_ApiKey",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Assistant_BaseUrl",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Assistant_Enabled",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Assistant_MaxSources",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Assistant_Model",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Assistant_TimeoutSeconds",
                table: "Settings");
        }
    }
}
