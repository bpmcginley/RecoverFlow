using Microsoft.AspNetCore.DataProtection;

namespace RecoverFlow.Api.Auth;

/// <summary>
/// Mints and validates the short-lived, signed tokens embedded in magic-link
/// sign-in emails. The token carries only the merchant id; DataProtection handles
/// tamper-proofing, and the time-limited protector enforces the expiry — so no
/// server-side token store is needed.
/// </summary>
public sealed class MagicLinkTokenService
{
    // Long enough to arrive and be clicked, short enough that a leaked link goes
    // stale quickly. Single-use hardening (a per-merchant nonce) can come later.
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly ITimeLimitedDataProtector protector;

    public MagicLinkTokenService(IDataProtectionProvider provider) =>
        protector = provider.CreateProtector("MagicLinkSignIn").ToTimeLimitedDataProtector();

    public string Mint(Guid merchantId) => protector.Protect(merchantId.ToString(), Lifetime);

    /// <summary>The merchant id, or null if the token is invalid, tampered, or expired.</summary>
    public Guid? Validate(string token)
    {
        try { return Guid.Parse(protector.Unprotect(token)); }
        catch { return null; }
    }
}
