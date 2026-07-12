using System.Net;

namespace RecoverFlow.Api.Auth;

/// <summary>Builds the passwordless sign-in email sent by <c>/auth/request-link</c>.</summary>
public static class LoginEmail
{
    public static (string Subject, string Html, string PlainText) Build(string companyName, string link)
    {
        const string subject = "Your RecoverFlow sign-in link";
        var company = WebUtility.HtmlEncode(companyName);

        var html = $"""
            <div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;max-width:480px;margin:0 auto;color:#14171f">
              <h2 style="margin:0 0 12px">Sign in to RecoverFlow</h2>
              <p style="color:#565d6d;line-height:1.5">Click below to open the {company} dashboard. This link expires in 15 minutes and can only be used from this email.</p>
              <p style="margin:24px 0"><a href="{link}" style="background:#5b5bf0;color:#fff;text-decoration:none;padding:12px 24px;border-radius:8px;font-weight:600;display:inline-block">Open my dashboard</a></p>
              <p style="color:#9aa0ac;font-size:13px;line-height:1.5">If the button doesn't work, paste this link into your browser:<br>{link}</p>
              <p style="color:#9aa0ac;font-size:13px">If you didn't request this, you can safely ignore this email.</p>
            </div>
            """;

        var plain =
            $"Sign in to RecoverFlow\n\n" +
            $"Open your {companyName} dashboard (link expires in 15 minutes):\n{link}\n\n" +
            "If you didn't request this, you can safely ignore this email.";

        return (subject, html, plain);
    }
}
