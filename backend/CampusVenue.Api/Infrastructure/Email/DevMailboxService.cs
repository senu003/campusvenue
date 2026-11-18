using System.Collections.Concurrent;
using CampusVenue.Api.Modules.Identity.Features.Auth;

namespace CampusVenue.Api.Infrastructure.Email;

/// <summary>
/// Development-only in-memory mailbox that intercepts sent emails and exposes them
/// to a local browser inbox. Never communicates with external networks or paid APIs.
/// </summary>
public class DevMailboxService : IEmailSender
{
    private readonly ConcurrentBag<DevEmailMessage> _messages = [];
    private readonly ILogger<DevMailboxService> _logger;

    public DevMailboxService(ILogger<DevMailboxService> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(string toEmail, string subject, string htmlMessage, CancellationToken cancellationToken = default)
    {
        var message = new DevEmailMessage(
            Id: Guid.NewGuid(),
            To: toEmail,
            Subject: subject,
            BodyHtml: htmlMessage,
            SentAtUtc: DateTime.UtcNow);

        _messages.Add(message);

        // Security requirement: Never log verification links or tokens
        _logger.LogInformation("Development email captured in local mailbox for recipient: {ToEmail}, Subject: {Subject}", toEmail, subject);

        return Task.CompletedTask;
    }

    public IReadOnlyList<DevEmailMessage> GetMessages()
    {
        return _messages.OrderByDescending(m => m.SentAtUtc).ToList();
    }

    public DevEmailMessage? GetMessage(Guid id)
    {
        return _messages.FirstOrDefault(m => m.Id == id);
    }

    public void Clear()
    {
        _messages.Clear();
    }
}
