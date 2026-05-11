using Azure.Identity;
using MailToBexio.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace MailToBexio.Services;

public interface IGraphMailService
{
    Task<IList<Message>> GetUnreadMessagesAsync(CancellationToken ct = default);
    Task MarkAsReadAsync(string messageId, CancellationToken ct = default);
    Task MoveToErrorFolderAsync(string messageId, CancellationToken ct = default);
}

public class GraphMailService : IGraphMailService
{
    private readonly GraphServiceClient _client;
    private readonly GraphSettings _settings;
    private readonly ILogger<GraphMailService> _logger;

    public GraphMailService(IOptions<GraphSettings> settings, ILogger<GraphMailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        var credential = new ClientSecretCredential(
            _settings.TenantId,
            _settings.ClientId,
            _settings.ClientSecret);

        _client = new GraphServiceClient(credential);
    }

    public async Task<IList<Message>> GetUnreadMessagesAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _client
                .Users[_settings.TargetMailboxUpn]
                .MailFolders
                .GetAsync(req =>
                {
                    req.QueryParameters.Filter = $"displayName eq '{_settings.MailFolderName}'";
                }, ct);

            var folder = result?.Value?.FirstOrDefault();
            if (folder is null)
            {
                _logger.LogWarning("Ordner '{Folder}' nicht gefunden in Postfach {Upn}",
                    _settings.MailFolderName, _settings.TargetMailboxUpn);
                return [];
            }

            var messages = await _client
                .Users[_settings.TargetMailboxUpn]
                .MailFolders[folder.Id]
                .Messages
                .GetAsync(req =>
                {
                    req.QueryParameters.Filter = "isRead eq false";
                    req.QueryParameters.Select = ["id", "subject", "body", "sender", "receivedDateTime"];
                    req.QueryParameters.Top = 25;
                }, ct);

            return messages?.Value ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen der Nachrichten aus Graph API");
            return [];
        }
    }

    public async Task MarkAsReadAsync(string messageId, CancellationToken ct = default)
    {
        try
        {
            await _client
                .Users[_settings.TargetMailboxUpn]
                .Messages[messageId]
                .PatchAsync(new Message { IsRead = true }, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Markieren der Nachricht {Id} als gelesen", messageId);
        }
    }

    public async Task MoveToErrorFolderAsync(string messageId, CancellationToken ct = default)
    {
        try
        {
            var folders = await _client
                .Users[_settings.TargetMailboxUpn]
                .MailFolders
                .GetAsync(req =>
                {
                    req.QueryParameters.Filter = $"displayName eq '{_settings.ErrorFolderName}'";
                }, ct);

            var errorFolder = folders?.Value?.FirstOrDefault();
            if (errorFolder is null)
            {
                _logger.LogWarning("Fehler-Ordner '{Folder}' nicht gefunden — Nachricht bleibt ungelesen",
                    _settings.ErrorFolderName);
                return;
            }

            await _client
                .Users[_settings.TargetMailboxUpn]
                .Messages[messageId]
                .Move
                .PostAsync(new Microsoft.Graph.Users.Item.Messages.Item.Move.MovePostRequestBody
                {
                    DestinationId = errorFolder.Id
                }, cancellationToken: ct);

            _logger.LogInformation("Nachricht {Id} in Fehler-Ordner verschoben", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Verschieben der Nachricht {Id} in Fehler-Ordner", messageId);
        }
    }
}
