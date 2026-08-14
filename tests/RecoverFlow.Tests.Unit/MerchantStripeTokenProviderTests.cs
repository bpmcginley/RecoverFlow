using Microsoft.EntityFrameworkCore;
using RecoverFlow.Application.Connect;
using RecoverFlow.Domain.Entities;
using RecoverFlow.Infrastructure.Persistence;

namespace RecoverFlow.Tests.Unit;

/// <summary>
/// Stripe Apps access tokens expire after an hour, so every background job that touches a
/// merchant's Stripe account now depends on this class getting the refresh right. Connect
/// tokens never expired, which is why none of this existed before v0.0.3.
/// </summary>
public class MerchantStripeTokenProviderTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private Merchant Seed(string? access, string? refresh, DateTime? expiresAt)
    {
        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            Email = "owner@acme.test",
            CompanyName = "Acme",
            StripeAccountId = "acct_123",
            EncryptedStripeAccessToken = access,
            EncryptedStripeRefreshToken = refresh,
            StripeAccessTokenExpiresAtUtc = expiresAt,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Merchants.Add(merchant);
        _db.SaveChanges();
        return merchant;
    }

    [Fact]
    public async Task A_live_token_is_returned_without_calling_stripe()
    {
        var merchant = Seed("at_live", "rt_1", DateTime.UtcNow.AddMinutes(50));
        var oauth = new FakeStripeOAuthClient();

        var token = await TestTokens.Provider(_db, oauth).GetAccessTokenAsync(merchant);

        Assert.Equal("at_live", token);
        Assert.Empty(oauth.RefreshedWith);
    }

    [Fact]
    public async Task A_null_expiry_means_a_legacy_connect_token_that_never_expires()
    {
        // Merchants who connected before the Stripe Apps migration have no expiry recorded.
        // Treating null as "expired long ago" would send every one of them through a refresh
        // they have no refresh token for, and break accounts that currently work fine.
        var merchant = Seed("at_connect", refresh: null, expiresAt: null);
        var oauth = new FakeStripeOAuthClient();

        var token = await TestTokens.Provider(_db, oauth).GetAccessTokenAsync(merchant);

        Assert.Equal("at_connect", token);
        Assert.Empty(oauth.RefreshedWith);
    }

    [Fact]
    public async Task An_expired_token_is_refreshed_and_the_rolled_refresh_token_is_persisted()
    {
        var merchant = Seed("at_stale", "rt_old", DateTime.UtcNow.AddMinutes(-1));
        var oauth = new FakeStripeOAuthClient
        {
            NextResult = new("acct_123", "at_fresh", "rt_new", null, DateTime.UtcNow.AddHours(1)),
        };

        var token = await TestTokens.Provider(_db, oauth).GetAccessTokenAsync(merchant);

        Assert.Equal("at_fresh", token);
        Assert.Equal(["rt_old"], oauth.RefreshedWith);

        // Stripe issues a new refresh token on every exchange and the old one is spent. Storing
        // the new one is the whole ballgame: miss it and the merchant silently falls off in an hour.
        var saved = await _db.Merchants.SingleAsync();
        Assert.Equal("at_fresh", saved.EncryptedStripeAccessToken);
        Assert.Equal("rt_new", saved.EncryptedStripeRefreshToken);
        Assert.True(saved.StripeAccessTokenExpiresAtUtc > DateTime.UtcNow.AddMinutes(50));
    }

    [Fact]
    public async Task A_token_inside_the_expiry_margin_is_refreshed_early()
    {
        // A token with two minutes left passes a naive check and then dies mid-request.
        var merchant = Seed("at_nearly_stale", "rt_old", DateTime.UtcNow.AddMinutes(2));
        var oauth = new FakeStripeOAuthClient();

        await TestTokens.Provider(_db, oauth).GetAccessTokenAsync(merchant);

        Assert.Single(oauth.RefreshedWith);
    }

    [Fact]
    public async Task An_expired_token_with_no_refresh_token_reports_the_merchant_as_unauthorized()
    {
        var merchant = Seed("at_stale", refresh: null, expiresAt: DateTime.UtcNow.AddMinutes(-1));

        var ex = await Assert.ThrowsAsync<MerchantNotAuthorizedException>(
            () => TestTokens.Provider(_db).GetAccessTokenAsync(merchant));

        Assert.Equal(merchant.Id, ex.MerchantId);
    }

    [Fact]
    public async Task A_merchant_who_never_finished_the_install_reports_as_unauthorized()
    {
        var merchant = Seed(access: null, refresh: null, expiresAt: null);

        await Assert.ThrowsAsync<MerchantNotAuthorizedException>(
            () => TestTokens.Provider(_db).GetAccessTokenAsync(merchant));
    }
}
