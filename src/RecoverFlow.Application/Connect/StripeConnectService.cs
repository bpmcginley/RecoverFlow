using Microsoft.EntityFrameworkCore;
using RecoverFlow.Application.Common;
using RecoverFlow.Domain.Entities;

namespace RecoverFlow.Application.Connect;

public sealed class StripeConnectService(
    IAppDbContext db,
    IStripeOAuthClient oauthClient,
    ITokenEncryptor encryptor)
{
    public async Task<Merchant> CompleteConnectionAsync(
        string code, string email, string companyName, CancellationToken ct = default)
    {
        var token = await oauthClient.ExchangeCodeAsync(code, ct);
        var encryptedAccessToken = encryptor.Encrypt(token.AccessToken);

        var merchant = await db.Merchants.SingleOrDefaultAsync(m => m.StripeAccountId == token.StripeAccountId, ct);
        if (merchant is null)
        {
            merchant = new Merchant
            {
                Id = Guid.NewGuid(),
                Email = email,
                CompanyName = companyName,
                StripeAccountId = token.StripeAccountId,
                CreatedAt = DateTime.UtcNow,
            };
            db.Merchants.Add(merchant);
        }

        merchant.EncryptedStripeAccessToken = encryptedAccessToken;
        await db.SaveChangesAsync(ct);

        return merchant;
    }
}
