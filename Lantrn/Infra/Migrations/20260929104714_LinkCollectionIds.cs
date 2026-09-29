using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lantrn.Infra.Migrations
{
    /// <inheritdoc />
    public partial class LinkCollectionIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Sources_CollectionId",
                table: "Sources",
                column: "CollectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Collections_CollectionId",
                table: "Documents",
                column: "CollectionId",
                principalTable: "Collections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sources_Collections_CollectionId",
                table: "Sources",
                column: "CollectionId",
                principalTable: "Collections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Collections_CollectionId",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_Sources_Collections_CollectionId",
                table: "Sources");

            migrationBuilder.DropIndex(
                name: "IX_Sources_CollectionId",
                table: "Sources");
        }
    }
}
