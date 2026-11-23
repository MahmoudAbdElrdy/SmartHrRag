using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartHr.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRetrievalEvalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Unanswerable",
                table: "RetrievalCases",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "EvaluationRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Unanswerable",
                table: "RetrievalCases");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "EvaluationRuns");
        }
    }
}
