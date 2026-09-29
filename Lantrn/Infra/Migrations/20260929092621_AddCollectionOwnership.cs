using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lantrn.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrivate",
                table: "Collections",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Collections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_OwnerId",
                table: "Collections",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Collections_AspNetUsers_OwnerId",
                table: "Collections",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Collections_AspNetUsers_OwnerId",
                table: "Collections");

            migrationBuilder.DropIndex(
                name: "IX_Collections_OwnerId",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "IsPrivate",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Collections");
        }
    }
}
