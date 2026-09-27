using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecoverFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialWaiverAndBillingPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "trial_waived_at_utc",
                table: "failed_payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_fee_invoices_merchant_id_period_label",
                table: "fee_invoices",
                columns: new[] { "merchant_id", "period_label" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_fee_invoices_merchant_id_period_label",
                table: "fee_invoices");

            migrationBuilder.DropColumn(
                name: "trial_waived_at_utc",
                table: "failed_payments");
        }
    }
}
