using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartHr.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkEmbeddingModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModel",
                table: "Chunks",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmbeddingModel",
                table: "Chunks");
        }
    }
}
