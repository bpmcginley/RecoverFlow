namespace RecoverFlow.Application.Common;

/// <summary>
/// Ambient merchant identity for the current scope. Null means the work is untenanted
/// (webhook processing, background jobs, the OAuth connect flow) and tenant query
/// filters are disabled.
/// </summary>
public interface ITenantContext
{
    Guid? MerchantId { get; }
}

/// <summary>Settable per-scope implementation; populated by request middleware.</summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? MerchantId { get; set; }
}
