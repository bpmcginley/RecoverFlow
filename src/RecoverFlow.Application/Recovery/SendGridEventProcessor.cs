using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RecoverFlow.Application.Common;

namespace RecoverFlow.Application.Recovery;

/// <summary>
/// Applies SendGrid event-webhook batches to the email sequence history: the first
/// "open"/"click" per entry stamps <c>OpenedAt</c>/<c>ClickedAt</c>. Runs as a Hangfire
/// job enqueued by the webhook controller after signature verification.
/// </summary>
public sealed class SendGridEventProcessor(
    IAppDbContext db,
    ILogger<SendGridEventProcessor> log)
{
    /// <summary>Custom-arg key carrying the EmailSequenceEntry id on tracked sends.
    /// SendGridEmailSender writes the same key onto outgoing messages.</summary>
    public const string TrackingArg = "rf_entry_id";

    public async Task ProcessAsync(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var kind = e.TryGetProperty("event", out var k) ? k.GetString() : null;
            if (kind is not ("open" or "click")) continue;

            // Apple Mail Privacy Protection prefetches every pixel; counting those as
            // opens would report engagement that never happened.
            if (e.TryGetProperty("sg_machine_open", out var m) && m.ValueKind == JsonValueKind.True)
                continue;

            if (!e.TryGetProperty(TrackingArg, out var idProp) ||
                !Guid.TryParse(idProp.GetString(), out var entryId))
                continue;

            var entry = await db.EmailSequences.FirstOrDefaultAsync(x => x.Id == entryId);
            if (entry is null)
            {
                log.LogWarning("SendGrid {Event} event for unknown sequence entry {EntryId}", kind, entryId);
                continue;
            }

            var at = e.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime
                : DateTime.UtcNow;

            if (kind == "open") entry.OpenedAt ??= at;
            else entry.ClickedAt ??= at;
        }

        await db.SaveChangesAsync();
    }
}
