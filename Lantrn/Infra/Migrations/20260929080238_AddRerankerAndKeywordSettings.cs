using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lantrn.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddRerankerAndKeywordSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Keywords_Language",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Reranker_ApiKey",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reranker_BaseUrl",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Reranker_Candidates",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Reranker_Enabled",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Reranker_Model",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Reranker_TimeoutSeconds",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Keywords_Language",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Reranker_ApiKey",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Reranker_BaseUrl",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Reranker_Candidates",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Reranker_Enabled",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Reranker_Model",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Reranker_TimeoutSeconds",
                table: "Settings");
        }
    }
}
