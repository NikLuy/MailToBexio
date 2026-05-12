using System.Net.Mail;
using MailToBexio.Configuration;
using MailToBexio.Models;
using MailToBexio.Services;
using MailToBexio.Services.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;

namespace MailToBexio.Workers;

public class MailProcessingWorker : BackgroundService
{
    private readonly IGraphMailService _graphService;
    private readonly IAIService _aiService;
    private readonly IBexioService _bexioService;
    private readonly WorkerSettings _settings;
    private readonly ILogger<MailProcessingWorker> _logger;

    public MailProcessingWorker(
        IGraphMailService graphService,
        IAIService aiService,
        IBexioService bexioService,
        IOptions<WorkerSettings> settings,
        ILogger<MailProcessingWorker> logger)
    {
        _graphService = graphService;
        _aiService = aiService;
        _bexioService = bexioService;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MailToBexio Worker gestartet - Intervall: {Min} Minuten", _settings.IntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessCycleAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(_settings.IntervalMinutes), stoppingToken);
        }
    }

    internal async Task ProcessCycleAsync(CancellationToken ct)
    {
        _logger.LogInformation("Verarbeitungszyklus gestartet: {Time}", DateTimeOffset.Now);

        var messages = await _graphService.GetUnreadMessagesAsync(ct);
        if (messages.Count == 0)
        {
            _logger.LogInformation("Keine neuen Nachrichten");
            return;
        }

        _logger.LogInformation("{Count} Nachricht(en) im Eingangsordner gefunden", messages.Count);

        foreach (var message in messages)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await ProcessMessageAsync(message, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Fehler beim Verarbeiten der Nachricht {Id} - Nachricht bleibt ungelesen",
                    message.Id);
            }
        }
    }

    private async Task ProcessMessageAsync(Message message, CancellationToken ct)
    {
        var messageId = message.Id!;
        _logger.LogInformation("Verarbeite Nachricht {Id}", messageId);

        var mailText = BuildExtractionInput(message);
        var customerData = await _aiService.ExtractCustomerInfoAsync(mailText, ct);
        ApplyEmailFallback(customerData, message);

        if (customerData is null || !customerData.IsValid())
        {
            _logger.LogWarning(
                "KI konnte keine validen Daten aus Nachricht {Id} extrahieren: Company={Company}, First={First}, Last={Last}, Email={Email}, TextLength={Length} - wird in Fehler-Ordner verschoben",
                messageId,
                customerData?.CompanyName,
                customerData?.FirstName,
                customerData?.LastName,
                customerData?.Email,
                mailText.Length);
            await _graphService.MoveToErrorFolderAsync(messageId, ct);
            return;
        }

        _logger.LogInformation("Extrahiert: {Company} / {First} {Last} <{Email}>",
            customerData.CompanyName, customerData.FirstName, customerData.LastName, customerData.Email);

        var result = await _bexioService.CreateContactIfNotExistsAsync(customerData, ct);

        if (result == BexioContactResult.Failed)
        {
            _logger.LogWarning("bexio konnte Nachricht {Id} nicht verarbeiten - Nachricht bleibt ungelesen", messageId);
            return;
        }

        await _graphService.MoveToProcessedFolderAsync(messageId, ct);
    }

    private static string BuildExtractionInput(Message message)
    {
        var sender = message.Sender?.EmailAddress;
        var from = message.From?.EmailAddress;
        var replyTo = message.ReplyTo?.FirstOrDefault()?.EmailAddress;

        return $"""
            Betreff: {message.Subject}
            Von Name: {from?.Name}
            Von E-Mail: {from?.Address}
            Reply-To Name: {replyTo?.Name}
            Reply-To E-Mail: {replyTo?.Address}
            Absender Name: {sender?.Name}
            Absender E-Mail: {sender?.Address}

            Body:
            {message.Body?.Content}
            """;
    }

    private static void ApplyEmailFallback(CustomerData? customerData, Message message)
    {
        if (customerData is null || !string.IsNullOrWhiteSpace(customerData.Email))
        {
            return;
        }

        customerData.Email = GetUsableEmail(
            message.ReplyTo?.Select(recipient => recipient.EmailAddress?.Address),
            [message.From?.EmailAddress?.Address, message.Sender?.EmailAddress?.Address]);
    }

    private static string? GetUsableEmail(params IEnumerable<string?>?[] sources)
    {
        foreach (var candidate in sources
            .Where(source => source is not null)
            .SelectMany(source => source!)
            .Where(IsUsableEmail))
        {
            return candidate!.Trim();
        }

        return null;
    }

    private static bool IsUsableEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var address = new MailAddress(value.Trim());
            var localPart = address.User.ToLowerInvariant();

            return localPart is not "noreply" and not "no-reply" and not "donotreply" and not "do-not-reply";
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
