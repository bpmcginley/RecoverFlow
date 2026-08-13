namespace RecoverFlow.Application.Common;

public interface IEmailSender
{
    /// <param name="trackingId">
    /// When set, the sender enables open/click tracking and attaches this id to the message
    /// so provider events can be matched back to an <c>EmailSequenceEntry</c>. Null (the
    /// default) sends with tracking off — sign-in links and audit reports stay untracked.
    /// </param>
    Task SendAsync(string to, string subject, string htmlBody, string plainTextBody,
        string? trackingId = null, CancellationToken ct = default);
}
