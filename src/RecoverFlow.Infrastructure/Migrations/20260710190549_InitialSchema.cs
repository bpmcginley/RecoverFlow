using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecoverFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "merchants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    company_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    stripe_account_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    encrypted_stripe_access_token = table.Column<string>(type: "text", nullable: true),
                    plan = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    settings_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_merchants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processed_webhook_events",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_webhook_events", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "failed_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stripe_invoice_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    stripe_subscription_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    stripe_customer_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    customer_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    decline_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    decline_reason = table.Column<string>(type: "text", nullable: true),
                    failure_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recovery_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    first_failed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    recovered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lost_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_failed_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_failed_payments_merchants_merchant_id",
                        column: x => x.merchant_id,
                        principalTable: "merchants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_update_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    failed_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    new_payment_method_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_update_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_card_update_sessions_failed_payments_failed_payment_id",
                        column: x => x.failed_payment_id,
                        principalTable: "failed_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_sequences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    failed_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_step = table.Column<int>(type: "integer", nullable: false),
                    email_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    clicked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resulted_in_recovery = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_sequences", x => x.id);
                    table.ForeignKey(
                        name: "fk_email_sequences_failed_payments_failed_payment_id",
                        column: x => x.failed_payment_id,
                        principalTable: "failed_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "retry_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    failed_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    scheduled_for = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    attempted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    decline_code_received = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retry_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_retry_attempts_failed_payments_failed_payment_id",
                        column: x => x.failed_payment_id,
                        principalTable: "failed_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_card_update_sessions_failed_payment_id",
                table: "card_update_sessions",
                column: "failed_payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_card_update_sessions_token",
                table: "card_update_sessions",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_sequences_failed_payment_id",
                table: "email_sequences",
                column: "failed_payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_failed_payments_merchant_id_stripe_invoice_id",
                table: "failed_payments",
                columns: new[] { "merchant_id", "stripe_invoice_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_failed_payments_status",
                table: "failed_payments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_merchants_stripe_account_id",
                table: "merchants",
                column: "stripe_account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_retry_attempts_failed_payment_id",
                table: "retry_attempts",
                column: "failed_payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_retry_attempts_scheduled_for",
                table: "retry_attempts",
                column: "scheduled_for");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_update_sessions");

            migrationBuilder.DropTable(
                name: "email_sequences");

            migrationBuilder.DropTable(
                name: "processed_webhook_events");

            migrationBuilder.DropTable(
                name: "retry_attempts");

            migrationBuilder.DropTable(
                name: "failed_payments");

            migrationBuilder.DropTable(
                name: "merchants");
        }
    }
}
