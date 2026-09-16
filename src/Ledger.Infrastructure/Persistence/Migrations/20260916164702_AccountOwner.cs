using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "owner_id",
                schema: "ledger",
                table: "accounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "legacy"); // существующие счета без владельца: доступ только администратору

            migrationBuilder.CreateIndex(
                name: "ix_accounts_owner",
                schema: "ledger",
                table: "accounts",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_accounts_owner",
                schema: "ledger",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "ledger",
                table: "accounts");
        }
    }
}
