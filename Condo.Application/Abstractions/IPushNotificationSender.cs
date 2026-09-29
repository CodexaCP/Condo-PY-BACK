namespace Condo.Application.Abstractions;

public interface IPushNotificationSender
{
    Task SendAsync(string deviceToken, string title, string body, IDictionary<string, string>? data, CancellationToken cancellationToken);
}
