using System.Security.Claims;

namespace RecoverFlow.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public const string MerchantIdClaim = "merchant_id";

    public static Guid? GetMerchantId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(MerchantIdClaim)?.Value, out var id) ? id : null;
}
