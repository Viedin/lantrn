using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lantrn.Infra.Migrations
{
    /// <inheritdoc />
    public partial class UseCollectionIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Collections_Collection",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_Sources_Collections_Collection",
                table: "Sources");

            migrationBuilder.DropIndex(
                name: "IX_Sources_Collection",
                table: "Sources");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Collections",
                table: "Collections");

            migrationBuilder.DropIndex(
                name: "IX_Collections_OwnerId",
                table: "Collections");

            migrationBuilder.RenameColumn(
                name: "Collection",
                table: "Sources",
                newName: "CollectionId");

            migrationBuilder.RenameColumn(
                name: "Collection",
                table: "Documents",
                newName: "CollectionId");

            migrationBuilder.RenameIndex(
                name: "IX_Documents_Collection_Source",
                table: "Documents",
                newName: "IX_Documents_CollectionId_Source");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "Collections",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_Collections",
                table: "Collections",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_OwnerId_Name",
                table: "Collections",
                columns: new[] { "OwnerId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Collections",
                table: "Collections");

            migrationBuilder.DropIndex(
                name: "IX_Collections_OwnerId_Name",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "Collections");

            migrationBuilder.RenameColumn(
                name: "CollectionId",
                table: "Sources",
                newName: "Collection");

            migrationBuilder.RenameColumn(
                name: "CollectionId",
                table: "Documents",
                newName: "Collection");

            migrationBuilder.RenameIndex(
                name: "IX_Documents_CollectionId_Source",
                table: "Documents",
                newName: "IX_Documents_Collection_Source");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Collections",
                table: "Collections",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Sources_Collection",
                table: "Sources",
                column: "Collection");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_OwnerId",
                table: "Collections",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Collections_Collection",
                table: "Documents",
                column: "Collection",
                principalTable: "Collections",
                principalColumn: "Name",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sources_Collections_Collection",
                table: "Sources",
                column: "Collection",
                principalTable: "Collections",
                principalColumn: "Name",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
