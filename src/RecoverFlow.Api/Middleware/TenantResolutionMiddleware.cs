using RecoverFlow.Api.Auth;
using RecoverFlow.Application.Common;

namespace RecoverFlow.Api.Middleware;

/// <summary>
/// Scopes the request to the signed-in merchant by copying their id from the auth
/// cookie's claim into the tenant context, which drives the EF query filters.
/// Unauthenticated requests (Stripe webhooks, the OAuth connect flow) and background
/// jobs carry no claim and run untenanted, which disables the filters.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http, TenantContext tenant)
    {
        if (http.User.GetMerchantId() is { } merchantId)
            tenant.MerchantId = merchantId;
        return next(http);
    }
}
