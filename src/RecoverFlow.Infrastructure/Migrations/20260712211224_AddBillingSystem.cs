using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecoverFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "stripe_platform_customer_id",
                table: "merchants",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "billed_at_utc",
                table: "failed_payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "fee_invoice_id",
                table: "failed_payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fee_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_label = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    billable_recovered_cents = table.Column<long>(type: "bigint", nullable: false),
                    recovered_case_count = table.Column<int>(type: "integer", nullable: false),
                    fee_cents = table.Column<long>(type: "bigint", nullable: false),
                    floor_top_up_cents = table.Column<long>(type: "bigint", nullable: false),
                    total_cents = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stripe_invoice_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    hosted_invoice_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_fee_invoices_merchants_merchant_id",
                        column: x => x.merchant_id,
                        principalTable: "merchants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_failed_payments_fee_invoice_id",
                table: "failed_payments",
                column: "fee_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_fee_invoices_merchant_id",
                table: "fee_invoices",
                column: "merchant_id");

            migrationBuilder.CreateIndex(
                name: "ix_fee_invoices_status",
                table: "fee_invoices",
                column: "status");

            migrationBuilder.AddForeignKey(
                name: "fk_failed_payments_fee_invoices_fee_invoice_id",
                table: "failed_payments",
                column: "fee_invoice_id",
                principalTable: "fee_invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_failed_payments_fee_invoices_fee_invoice_id",
                table: "failed_payments");

            migrationBuilder.DropTable(
                name: "fee_invoices");

            migrationBuilder.DropIndex(
                name: "ix_failed_payments_fee_invoice_id",
                table: "failed_payments");

            migrationBuilder.DropColumn(
                name: "stripe_platform_customer_id",
                table: "merchants");

            migrationBuilder.DropColumn(
                name: "billed_at_utc",
                table: "failed_payments");

            migrationBuilder.DropColumn(
                name: "fee_invoice_id",
                table: "failed_payments");
        }
    }
}
