using RecoverFlow.Application.Billing;
using RecoverFlow.Application.Recovery;

namespace RecoverFlow.Tests.Unit;

internal sealed class FakeInvoicePayer : IStripeInvoicePayer
{
    public InvoicePayResult NextResult { get; set; } = InvoicePayResult.Success;
    public List<(string InvoiceId, string Account, string IdempotencyKey)> Calls { get; } = [];

    public Task<InvoicePayResult> PayInvoiceAsync(
        string invoiceId, string stripeAccountId, string idempotencyKey, CancellationToken ct = default)
    {
        Calls.Add((invoiceId, stripeAccountId, idempotencyKey));
        return Task.FromResult(NextResult);
    }
}

internal sealed class FakeRetryJobScheduler : IRetryJobScheduler
{
    public List<(Guid AttemptId, DateTime RunAtUtc)> Scheduled { get; } = [];

    public void ScheduleAttempt(Guid attemptId, DateTime runAtUtc) => Scheduled.Add((attemptId, runAtUtc));
}

internal sealed class FakePlatformFeeInvoicer : IPlatformFeeInvoicer
{
    public PlatformCustomerResult NextCustomerResult { get; set; } = PlatformCustomerResult.Success("cus_fake");
    public FeeInvoiceSendResult NextSendResult { get; set; } = new(true, "in_fake", "https://invoice.example/fake", null);
    /// <summary>When non-empty, dequeued per Send call (multi-merchant scenarios); falls back to NextSendResult.</summary>
    public Queue<FeeInvoiceSendResult> SendResults { get; } = [];

    public List<(Guid MerchantId, string Email, string CompanyName)> CustomerCalls { get; } = [];
    public List<(string CustomerId, Guid FeeInvoiceId, IReadOnlyList<FeeInvoiceLine> Lines, string? KnownStripeInvoiceId)> SendCalls { get; } = [];

    public Task<PlatformCustomerResult> EnsureCustomerAsync(
        Guid merchantId, string email, string companyName, CancellationToken ct = default)
    {
        CustomerCalls.Add((merchantId, email, companyName));
        return Task.FromResult(NextCustomerResult);
    }

    public Task<FeeInvoiceSendResult> SendFeeInvoiceAsync(
        string customerId, Guid feeInvoiceId, IReadOnlyList<FeeInvoiceLine> lines,
        string currency, int daysUntilDue, string? knownStripeInvoiceId, CancellationToken ct = default)
    {
        SendCalls.Add((customerId, feeInvoiceId, lines, knownStripeInvoiceId));
        return Task.FromResult(SendResults.Count > 0 ? SendResults.Dequeue() : NextSendResult);
    }
}
