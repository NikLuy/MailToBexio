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
    Task MoveToProcessedFolderAsync(string messageId, CancellationToken ct = default);
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
            var folder = await FindMailFolderAsync(_settings.MailFolderName, ct);
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
                    req.QueryParameters.Select = ["id", "subject", "body", "sender", "from", "replyTo", "receivedDateTime"];
                    req.QueryParameters.Top = 25;
                    req.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
                }, ct);

            return messages?.Value ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen der Nachrichten aus Graph API");
            return [];
        }
    }

    public async Task MoveToProcessedFolderAsync(string messageId, CancellationToken ct = default)
    {
        try
        {
            await MoveMessageToFolderAsync(messageId, _settings.ProcessedFolderName, "Done", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Verschieben der Nachricht {Id} in Done-Ordner", messageId);
        }
    }

    public async Task MoveToErrorFolderAsync(string messageId, CancellationToken ct = default)
    {
        try
        {
            await MoveMessageToFolderAsync(messageId, _settings.ErrorFolderName, "Fault", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Verschieben der Nachricht {Id} in Fehler-Ordner", messageId);
        }
    }

    private async Task MoveMessageToFolderAsync(
        string messageId,
        string folderName,
        string folderRole,
        CancellationToken ct)
    {
        var destinationFolder = await GetOrCreateDestinationFolderAsync(folderName, ct);
        if (destinationFolder is null)
        {
            _logger.LogWarning("{Role}-Ordner '{Folder}' nicht gefunden unter '{Parent}' - Nachricht bleibt ungelesen",
                folderRole, folderName, _settings.MailFolderName);
            return;
        }

        await _client
            .Users[_settings.TargetMailboxUpn]
            .Messages[messageId]
            .Move
            .PostAsync(new Microsoft.Graph.Users.Item.Messages.Item.Move.MovePostRequestBody
            {
                DestinationId = destinationFolder.Id
            }, cancellationToken: ct);

        _logger.LogInformation("Nachricht {Id} in {Role}-Ordner '{Folder}' verschoben",
            messageId, folderRole, destinationFolder.DisplayName);
    }

    private async Task<MailFolder?> GetOrCreateDestinationFolderAsync(string folderName, CancellationToken ct)
    {
        var parts = folderName
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length > 1)
        {
            return await GetOrCreateMailFolderByPathAsync(parts, ct);
        }

        var sourceFolder = await FindMailFolderAsync(_settings.MailFolderName, ct);
        if (sourceFolder?.Id is null)
        {
            return null;
        }

        var childFolderName = folderName.Trim();
        var destination = await FindDirectChildFolderByNameAsync(sourceFolder.Id, childFolderName, ct);
        return destination ?? await CreateChildFolderAsync(sourceFolder.Id, childFolderName, ct);
    }

    private async Task<MailFolder?> FindMailFolderAsync(string folderName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

        var folders = await _client
            .Users[_settings.TargetMailboxUpn]
            .MailFolders
            .GetAsync(req =>
            {
                req.QueryParameters.Select = ["id", "displayName", "childFolderCount"];
                req.QueryParameters.Top = 100;
            }, ct);

        var parts = folderName
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length > 1
            ? await FindMailFolderByPathAsync(folders?.Value, parts, 0, ct)
            : await FindMailFolderByNameAsync(folders?.Value, folderName.Trim(), ct);
    }

    private async Task<MailFolder?> GetOrCreateMailFolderByPathAsync(IReadOnlyList<string> parts, CancellationToken ct)
    {
        if (parts.Count == 0)
        {
            return null;
        }

        var current = await FindTopLevelFolderByNameAsync(parts[0], ct);
        if (current is null)
        {
            current = await _client
                .Users[_settings.TargetMailboxUpn]
                .MailFolders
                .PostAsync(new MailFolder { DisplayName = parts[0] }, cancellationToken: ct);

            _logger.LogInformation("Mailordner '{Folder}' erstellt", parts[0]);
        }

        for (var index = 1; index < parts.Count; index++)
        {
            if (current?.Id is null)
            {
                return null;
            }

            var child = await FindDirectChildFolderByNameAsync(current.Id, parts[index], ct);
            current = child ?? await CreateChildFolderAsync(current.Id, parts[index], ct);
        }

        return current;
    }

    private async Task<MailFolder?> FindTopLevelFolderByNameAsync(string folderName, CancellationToken ct)
    {
        var folders = await _client
            .Users[_settings.TargetMailboxUpn]
            .MailFolders
            .GetAsync(req =>
            {
                req.QueryParameters.Select = ["id", "displayName", "childFolderCount"];
                req.QueryParameters.Top = 100;
            }, ct);

        return folders?.Value?.FirstOrDefault(folder =>
            string.Equals(folder.DisplayName, folderName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<MailFolder?> FindDirectChildFolderByNameAsync(
        string parentFolderId,
        string folderName,
        CancellationToken ct)
    {
        var childFolders = await GetChildFoldersAsync(parentFolderId, ct);
        return childFolders.FirstOrDefault(folder =>
            string.Equals(folder.DisplayName, folderName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<MailFolder?> CreateChildFolderAsync(
        string parentFolderId,
        string folderName,
        CancellationToken ct)
    {
        var folder = await _client
            .Users[_settings.TargetMailboxUpn]
            .MailFolders[parentFolderId]
            .ChildFolders
            .PostAsync(new MailFolder { DisplayName = folderName }, cancellationToken: ct);

        _logger.LogInformation("Mailordner '{Folder}' erstellt", folderName);
        return folder;
    }

    private async Task<MailFolder?> FindMailFolderByNameAsync(
        IEnumerable<MailFolder>? folders,
        string folderName,
        CancellationToken ct)
    {
        if (folders is null)
        {
            return null;
        }

        var folderList = folders.ToList();
        var match = folderList.FirstOrDefault(folder =>
            string.Equals(folder.DisplayName, folderName, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            return match;
        }

        foreach (var folder in folderList.Where(folder => !string.IsNullOrWhiteSpace(folder.Id)))
        {
            var childMatch = await FindMailFolderByNameAsync(
                await GetChildFoldersAsync(folder.Id!, ct),
                folderName,
                ct);

            if (childMatch is not null)
            {
                return childMatch;
            }
        }

        return null;
    }

    private async Task<MailFolder?> FindMailFolderByPathAsync(
        IEnumerable<MailFolder>? folders,
        IReadOnlyList<string> parts,
        int index,
        CancellationToken ct)
    {
        if (folders is null || index >= parts.Count)
        {
            return null;
        }

        var folder = folders.FirstOrDefault(candidate =>
            string.Equals(candidate.DisplayName, parts[index], StringComparison.OrdinalIgnoreCase));

        if (folder is null)
        {
            return null;
        }

        if (index == parts.Count - 1)
        {
            return folder;
        }

        return string.IsNullOrWhiteSpace(folder.Id)
            ? null
            : await FindMailFolderByPathAsync(await GetChildFoldersAsync(folder.Id, ct), parts, index + 1, ct);
    }

    private async Task<IList<MailFolder>> GetChildFoldersAsync(string folderId, CancellationToken ct)
    {
        var result = await _client
            .Users[_settings.TargetMailboxUpn]
            .MailFolders[folderId]
            .ChildFolders
            .GetAsync(req =>
            {
                req.QueryParameters.Select = ["id", "displayName", "childFolderCount"];
                req.QueryParameters.Top = 100;
            }, ct);

        return result?.Value ?? [];
    }
}
