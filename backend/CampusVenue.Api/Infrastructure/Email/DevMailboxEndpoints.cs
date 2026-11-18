using System.Net;
using System.Text;
using CampusVenue.Api.Modules.Identity.Features.Auth;

namespace CampusVenue.Api.Infrastructure.Email;

/// <summary>
/// Development-only endpoints providing a browser-accessible mailbox UI and JSON API.
/// </summary>
public static class DevMailboxEndpoints
{
    public static IEndpointRouteBuilder MapDevMailboxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/dev/mailbox")
            .WithTags("Development Mailbox");

        // 1. JSON List Endpoint
        group.MapGet("/", (DevMailboxService mailbox) =>
        {
            return Results.Ok(mailbox.GetMessages());
        })
        .WithName("GetDevMailboxMessages")
        .WithSummary("Retrieve captured development emails")
        .Produces<IReadOnlyList<DevEmailMessage>>(StatusCodes.Status200OK);

        // 2. JSON Single Message Endpoint
        group.MapGet("/{id:guid}", (Guid id, DevMailboxService mailbox) =>
        {
            var msg = mailbox.GetMessage(id);
            return msg != null ? Results.Ok(msg) : Results.NotFound();
        })
        .WithName("GetDevMailboxMessageById")
        .Produces<DevEmailMessage>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // 3. Clear Mailbox Endpoint
        group.MapPost("/clear", (DevMailboxService mailbox) =>
        {
            mailbox.Clear();
            return Results.Ok(new { message = "Development mailbox cleared" });
        })
        .WithName("ClearDevMailbox");

        // 4. Standalone HTML Browser Inbox View
        endpoints.MapGet("/dev/mailbox", (DevMailboxService mailbox) =>
        {
            var messages = mailbox.GetMessages();
            var html = RenderMailboxHtml(messages);
            return Results.Content(html, "text/html; charset=utf-8");
        })
        .WithName("DevMailboxHtmlView")
        .WithSummary("Browser UI for viewing locally captured verification emails")
        .ExcludeFromDescription();

        return endpoints;
    }

    private static string RenderMailboxHtml(IReadOnlyList<DevEmailMessage> messages)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>CampusVenue — Local Development Mailbox</title>
                <style>
                    :root { --primary: #0284c7; --bg: #f8fafc; --card: #ffffff; --border: #e2e8f0; --text: #0f172a; --muted: #64748b; }
                    body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; background: var(--bg); color: var(--text); margin: 0; padding: 24px; }
                    .container { max-width: 960px; margin: 0 auto; }
                    .header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 24px; padding-bottom: 16px; border-bottom: 1px solid var(--border); }
                    .badge { display: inline-block; padding: 4px 10px; font-size: 12px; font-weight: 700; background: #e0f2fe; color: #0369a1; border-radius: 9999px; }
                    .card { background: var(--card); border: 1px solid var(--border); border-radius: 12px; box-shadow: 0 1px 3px rgba(0,0,0,0.05); margin-bottom: 20px; overflow: hidden; }
                    .card-header { padding: 16px 20px; border-bottom: 1px solid var(--border); display: flex; justify-content: space-between; align-items: center; background: #fdfdfd; }
                    .card-body { padding: 24px 20px; }
                    .btn { display: inline-block; padding: 8px 16px; background: var(--primary); color: #fff; text-decoration: none; border-radius: 6px; font-size: 13px; font-weight: 600; cursor: pointer; border: none; }
                    .btn:hover { background: #0369a1; }
                    .btn-outline { background: transparent; color: var(--muted); border: 1px solid var(--border); }
                    .btn-outline:hover { background: #f1f5f9; color: var(--text); }
                    .empty-state { text-align: center; padding: 48px 20px; color: var(--muted); }
                    .email-meta { font-size: 13px; color: var(--muted); margin-bottom: 12px; }
                    .email-body { border: 1px dashed var(--border); border-radius: 8px; padding: 20px; background: #fafafa; }
                    .action-bar { display: flex; gap: 10px; }
                </style>
            </head>
            <body>
                <div class="container">
                    <div class="header">
                        <div>
                            <h1 style="margin: 0; font-size: 24px; font-weight: 800;">CampusVenue Mailbox</h1>
                            <p style="margin: 4px 0 0; color: var(--muted); font-size: 13px;">Local development inbox for email verification</p>
                        </div>
                        <div class="action-bar">
                            <span class="badge">Development Only</span>
                            <button onclick="location.reload()" class="btn btn-outline">&#8635; Refresh</button>
                            <form method="POST" action="/api/dev/mailbox/clear" style="display:inline;" onsubmit="setTimeout(() => location.reload(), 150)">
                                <button type="submit" class="btn btn-outline" style="color: #e11d48; border-color: #fecdd3;">Clear Inbox</button>
                            </form>
                        </div>
                    </div>
            """);

        if (messages.Count == 0)
        {
            sb.Append("""
                    <div class="card">
                        <div class="empty-state">
                            <div style="font-size: 36px; margin-bottom: 12px;">&#128236;</div>
                            <h3 style="margin: 0 0 6px; color: var(--text);">Inbox is empty</h3>
                            <p style="margin: 0; font-size: 14px;">Sign up with a new account on CampusVenue to trigger a verification email.</p>
                        </div>
                    </div>
                """);
        }
        else
        {
            foreach (var msg in messages)
            {
                sb.Append($"""
                    <div class="card">
                        <div class="card-header">
                            <div>
                                <span style="font-weight: 700; font-size: 15px;">{WebUtility.HtmlEncode(msg.Subject)}</span>
                                <div class="email-meta" style="margin: 4px 0 0;">To: <strong>{WebUtility.HtmlEncode(msg.To)}</strong> &bull; {msg.SentAtUtc:yyyy-MM-dd HH:mm:ss} UTC</div>
                            </div>
                        </div>
                        <div class="card-body">
                            <div class="email-body">
                                {msg.BodyHtml}
                            </div>
                        </div>
                    </div>
                """);
            }
        }

        sb.Append("""
                </div>
            </body>
            </html>
            """);

        return sb.ToString();
    }
}
