using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication1.Migrations
{
    public partial class AddReservationAccessKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessKey",
                table: "Reservations",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE Reservations
SET AccessKey = CONCAT('AK-', LEFT(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''), 12))
WHERE AccessKey IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "AccessKey",
                table: "Reservations",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(24)",
                oldMaxLength: 24,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_AccessKey",
                table: "Reservations",
                column: "AccessKey",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_AccessKey",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "AccessKey",
                table: "Reservations");
        }
    }
}
