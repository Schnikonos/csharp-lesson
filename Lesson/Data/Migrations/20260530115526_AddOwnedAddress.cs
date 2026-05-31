using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lesson.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnedAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address_City",
                table: "BankAccounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_Country",
                table: "BankAccounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_PostalCode",
                table: "BankAccounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_Street",
                table: "BankAccounts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address_City",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "Address_Country",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "Address_PostalCode",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "Address_Street",
                table: "BankAccounts");
        }
    }
}
