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
