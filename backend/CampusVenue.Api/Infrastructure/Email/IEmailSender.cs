namespace CampusVenue.Api.Infrastructure.Email;

/// <summary>
/// Abstraction for transmitting transactional and confirmation emails.
/// </summary>
public interface IEmailSender
{
    Task SendEmailAsync(string toEmail, string subject, string htmlMessage, CancellationToken cancellationToken = default);
}
