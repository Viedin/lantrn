using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lantrn.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Embeddings_ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    Embeddings_BaseUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Embeddings_BatchSize = table.Column<int>(type: "INTEGER", nullable: false),
                    Embeddings_Dimensions = table.Column<int>(type: "INTEGER", nullable: true),
                    Embeddings_Model = table.Column<string>(type: "TEXT", nullable: false),
                    Embeddings_QueryPrefix = table.Column<string>(type: "TEXT", nullable: false),
                    Embeddings_TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    Vision_ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    Vision_BaseUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Vision_Model = table.Column<string>(type: "TEXT", nullable: false),
                    Vision_TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
