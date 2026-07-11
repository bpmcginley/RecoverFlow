using RecoverFlow.Application.Common;

namespace RecoverFlow.Api.Middleware;

/// <summary>
/// STOPGAP until real authentication lands: trusts an X-Merchant-Id header to identify
/// the tenant. Requests without the header (Stripe webhooks, the OAuth connect flow)
/// and background jobs run untenanted, which disables the tenant query filters.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Merchant-Id";

    public Task InvokeAsync(HttpContext http, TenantContext tenant)
    {
        if (Guid.TryParse(http.Request.Headers[HeaderName], out var merchantId))
            tenant.MerchantId = merchantId;
        return next(http);
    }
}
