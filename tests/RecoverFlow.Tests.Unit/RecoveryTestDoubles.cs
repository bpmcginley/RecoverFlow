using Microsoft.Extensions.Logging.Abstractions;
using RecoverFlow.Application.Backtest;
using RecoverFlow.Application.Billing;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Connect;
using RecoverFlow.Application.Recovery;

namespace RecoverFlow.Tests.Unit;

internal sealed class FakeHistoricalInvoiceReader : IHistoricalInvoiceReader
{
    public List<HistoricalFailedInvoice> Invoices { get; } = [];
    public Exception? Throw { get; set; }
    public List<(string AccessToken, DateTime Since, int Max)> Calls { get; } = [];

    public Task<IReadOnlyList<HistoricalFailedInvoice>> ListUncollectedInvoicesAsync(
        string accessToken, DateTime sinceUtc, int maxInvoices, CancellationToken ct = default)
    {
        Calls.Add((accessToken, sinceUtc, maxInvoices));
        if (Throw is not null) throw Throw;
        return Task.FromResult<IReadOnlyList<HistoricalFailedInvoice>>(Invoices);
    }
}

/// <summary>Round-trips plaintext, so a test asserting on a stored token reads what it seeded.</summary>
internal sealed class PassThroughTokenEncryptor : ITokenEncryptor
{
    public string Encrypt(string plaintext) => plaintext;
    public string Decrypt(string ciphertext) => ciphertext;
}

internal sealed class FakeStripeOAuthClient : IStripeOAuthClient
{
    public StripeOAuthTokenResult NextResult { get; set; } =
        new("acct_123", "sk_refreshed", "rt_new", null, DateTime.UtcNow.AddHours(1));
    public List<string> RefreshedWith { get; } = [];

    public Task<StripeOAuthTokenResult> ExchangeCodeAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(NextResult);

    public Task<StripeOAuthTokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        RefreshedWith.Add(refreshToken);
        return Task.FromResult(NextResult);
    }
}

internal static class TestTokens
{
    public static MerchantStripeTokenProvider Provider(IAppDbContext db, IStripeOAuthClient? oauth = null) =>
        new(db, oauth ?? new FakeStripeOAuthClient(), new PassThroughTokenEncryptor(),
            NullLogger<MerchantStripeTokenProvider>.Instance);
}

internal sealed class FakeBacktestJobScheduler : IBacktestJobScheduler
{
    public List<Guid> Enqueued { get; } = [];
    public void Enqueue(Guid backtestId) => Enqueued.Add(backtestId);
}

internal sealed class FakeInvoicePayer : IStripeInvoicePayer
{
    public InvoicePayResult NextResult { get; set; } = InvoicePayResult.Success;
    public List<(string InvoiceId, string AccessToken, string IdempotencyKey)> Calls { get; } = [];

    public Task<InvoicePayResult> PayInvoiceAsync(
        string invoiceId, string accessToken, string idempotencyKey, CancellationToken ct = default)
    {
        Calls.Add((invoiceId, accessToken, idempotencyKey));
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
