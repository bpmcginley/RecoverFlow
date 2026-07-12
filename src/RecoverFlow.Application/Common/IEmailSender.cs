namespace RecoverFlow.Application.Common;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, string plainTextBody, CancellationToken ct = default);
}
