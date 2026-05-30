using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Lesson.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BankAccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Transactions",
                columns: new[] { "Id", "Amount", "BankAccountId", "Description", "OccurredAt", "Type" },
                values: new object[,]
                {
                    { 1, 5000m, 1, "Salary", new DateTime(2024, 1, 5, 0, 0, 0, 0, DateTimeKind.Utc), "Credit" },
                    { 2, 1200m, 1, "Rent", new DateTime(2024, 1, 10, 0, 0, 0, 0, DateTimeKind.Utc), "Debit" },
                    { 3, 300m, 1, "Groceries", new DateTime(2024, 1, 15, 0, 0, 0, 0, DateTimeKind.Utc), "Debit" },
                    { 4, 10000m, 2, "Bonus", new DateTime(2024, 1, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Credit" },
                    { 5, 2500m, 2, "Car payment", new DateTime(2024, 1, 8, 0, 0, 0, 0, DateTimeKind.Utc), "Debit" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BankAccountId",
                table: "Transactions",
                column: "BankAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}
