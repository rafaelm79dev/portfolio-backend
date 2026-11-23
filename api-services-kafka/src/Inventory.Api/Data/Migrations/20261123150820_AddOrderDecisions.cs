using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_decisions",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reserved = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_decisions", x => x.order_id);
                });

            // Orders reserved before this table existed count as decided, so a repeated message for them is
            // answered instead of reserving again. (Rejected orders left no trace and cannot be recovered.)
            migrationBuilder.Sql(
                "INSERT INTO order_decisions (order_id, reserved, reason, decided_at) " +
                "SELECT order_id, TRUE, NULL, MIN(created_at) FROM stock_reservations GROUP BY order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_decisions");
        }
    }
}
