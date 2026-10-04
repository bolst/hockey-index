using System.Text.Json;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Integrations.Email;

public sealed record EmailMessage(string To, string Subject, string TextBody);

public interface IEmailSender
{
    Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed class PostmarkOptions
{
    public const string SectionName = "Postmark";

    public string ServerToken { get; set; } = "";
    public string From { get; set; } = "Hockey Index <no-reply@hockeyindex.com>";
    public string MessageStream { get; set; } = "outbound";
    public Uri BaseUrl { get; set; } = new("https://api.postmarkapp.com/");
}

public sealed partial class PostmarkEmailSender(HttpClient http, IOptions<PostmarkOptions> options, ILogger<PostmarkEmailSender> logger) : IEmailSender
{
    private static readonly JsonSerializerOptions PostmarkJson = new() { PropertyNamingPolicy = null };

    public async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.BaseUrl, "email"))
        {
            Content = JsonContent.Create(new
            {
                settings.From,
                message.To,
                message.Subject,
                message.TextBody,
                settings.MessageStream,
            }, options: PostmarkJson),
        };
        request.Headers.Add("X-Postmark-Server-Token", settings.ServerToken);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LogFailure(logger, (int)response.StatusCode);
        }

        return response.IsSuccessStatusCode;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Postmark send failed with status {StatusCode}")]
    private static partial void LogFailure(ILogger logger, int statusCode);
}
