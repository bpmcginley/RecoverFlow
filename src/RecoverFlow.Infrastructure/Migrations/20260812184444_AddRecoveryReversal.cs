using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecoverFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecoveryReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "reversal_credit_cents",
                table: "fee_invoices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "billed_base_cents",
                table: "failed_payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "billed_fee_cents",
                table: "failed_payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "reversal_credited_cents",
                table: "failed_payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "reversal_reason",
                table: "failed_payments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "reversed_amount_cents",
                table: "failed_payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "reversed_at_utc",
                table: "failed_payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stripe_charge_id",
                table: "failed_payments",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_failed_payments_stripe_charge_id",
                table: "failed_payments",
                column: "stripe_charge_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_failed_payments_stripe_charge_id",
                table: "failed_payments");

            migrationBuilder.DropColumn(
                name: "reversal_credit_cents",
                table: "fee_invoices");

            migrationBuilder.DropColumn(
                name: "billed_base_cents",
                table: "failed_payments");

            migrationBuilder.DropColumn(
                name: "billed_fee_cents",
                table: "failed_payments");

            migrationBuilder.DropColumn(
                name: "reversal_credited_cents",
                table: "failed_payments");

            migrationBuilder.DropColumn(
                name: "reversal_reason",
                table: "failed_payments");

            migrationBuilder.DropColumn(
                name: "reversed_amount_cents",
                table: "failed_payments");

            migrationBuilder.DropColumn(
                name: "reversed_at_utc",
                table: "failed_payments");

            migrationBuilder.DropColumn(
                name: "stripe_charge_id",
                table: "failed_payments");
        }
    }
}
