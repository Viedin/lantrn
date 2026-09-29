using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lantrn.Infra.Migrations
{
    /// <inheritdoc />
    public partial class TrackWhoAddedContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddedById",
                table: "Sources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddedById",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sources_AddedById",
                table: "Sources",
                column: "AddedById");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_AddedById",
                table: "Documents",
                column: "AddedById");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_AspNetUsers_AddedById",
                table: "Documents",
                column: "AddedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Sources_AspNetUsers_AddedById",
                table: "Sources",
                column: "AddedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_AspNetUsers_AddedById",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_Sources_AspNetUsers_AddedById",
                table: "Sources");

            migrationBuilder.DropIndex(
                name: "IX_Sources_AddedById",
                table: "Sources");

            migrationBuilder.DropIndex(
                name: "IX_Documents_AddedById",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AddedById",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "AddedById",
                table: "Documents");
        }
    }
}
