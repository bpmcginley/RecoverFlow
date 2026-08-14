using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecoverFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeAppOAuthTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "encrypted_stripe_refresh_token",
                table: "merchants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "stripe_access_token_expires_at_utc",
                table: "merchants",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "encrypted_stripe_refresh_token",
                table: "merchants");

            migrationBuilder.DropColumn(
                name: "stripe_access_token_expires_at_utc",
                table: "merchants");
        }
    }
}
