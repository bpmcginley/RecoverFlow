using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain.Entities;

namespace RecoverFlow.Application.Connect;

/// <summary>
/// Raised when a merchant has no usable Stripe credential: they never finished the install,
/// or they uninstalled the app and the refresh token died with the grant. Distinct from a
/// transient Stripe error, because retrying will not fix it — the merchant has to reinstall.
/// </summary>
public sealed class MerchantNotAuthorizedException(Guid merchantId, string reason)
    : Exception($"Merchant {merchantId} has no usable Stripe authorization: {reason}")
{
    public Guid MerchantId { get; } = merchantId;
}

/// <summary>
/// Hands out a live Stripe access token for a merchant, refreshing it when it has expired.
///
/// Stripe Apps access tokens last one hour, so a background job that runs days after the
/// install cannot use whatever was stored at install time. Connect tokens never expired,
/// which is why nothing needed this before; merchants connected under Connect have a null
/// expiry and are passed straight through.
/// </summary>
public sealed class MerchantStripeTokenProvider(
    IAppDbContext db,
    IStripeOAuthClient oauthClient,
    ITokenEncryptor encryptor,
    ILogger<MerchantStripeTokenProvider> log)
{
    // Refresh slightly early. A token that passes the check and then expires part-way through
    // a paging loop fails the call, and our clock is not Stripe's.
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(5);

    // Two jobs for the same merchant can wake up together, and Stripe issues a new refresh
    // token on every exchange — so a concurrent double-refresh risks persisting the loser's
    // now-superseded token. This serialises refreshes per merchant. It only covers one
    // process: if the API is ever scaled past a single instance this needs a database lock.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> RefreshLocks = new();

    public async Task<string> GetAccessTokenAsync(Merchant merchant, CancellationToken ct = default)
    {
        if (merchant.EncryptedStripeAccessToken is null)
            throw new MerchantNotAuthorizedException(merchant.Id, "no access token stored");

        if (!IsExpired(merchant)) return encryptor.Decrypt(merchant.EncryptedStripeAccessToken);

        var gate = RefreshLocks.GetOrAdd(merchant.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Another job may have refreshed while we queued, which also means merchant's
            // fields have already been updated in place by that call.
            if (!IsExpired(merchant)) return encryptor.Decrypt(merchant.EncryptedStripeAccessToken!);
            return await RefreshAsync(merchant, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsExpired(Merchant m) =>
        m.StripeAccessTokenExpiresAtUtc is { } expiry && DateTime.UtcNow >= expiry - ExpiryMargin;

    private async Task<string> RefreshAsync(Merchant merchant, CancellationToken ct)
    {
        if (merchant.EncryptedStripeRefreshToken is null)
            throw new MerchantNotAuthorizedException(merchant.Id, "access token expired and no refresh token stored");

        var token = await oauthClient.RefreshAsync(
            encryptor.Decrypt(merchant.EncryptedStripeRefreshToken), ct);

        Store(merchant, token, encryptor);
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Refreshed Stripe access token for merchant {MerchantId}, valid until {ExpiresAtUtc}",
            merchant.Id, merchant.StripeAccessTokenExpiresAtUtc);

        return token.AccessToken;
    }

    /// <summary>
    /// Writes a token exchange onto the merchant. Shared with the install flow so both paths
    /// keep the same rule: whatever refresh token came back replaces the one we spent.
    /// </summary>
    internal static void Store(Merchant merchant, StripeOAuthTokenResult token, ITokenEncryptor encryptor)
    {
        merchant.EncryptedStripeAccessToken = encryptor.Encrypt(token.AccessToken);
        merchant.StripeAccessTokenExpiresAtUtc = token.ExpiresAtUtc;
        if (token.RefreshToken is not null)
            merchant.EncryptedStripeRefreshToken = encryptor.Encrypt(token.RefreshToken);
    }
}
