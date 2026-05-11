using MailToBexio.Configuration;
using MailToBexio.Services;
using MailToBexio.Services.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        _logger.LogInformation("MailToBexio Worker gestartet — Intervall: {Min} Minuten", _settings.IntervalMinutes);

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

        _logger.LogInformation("{Count} neue Nachricht(en) gefunden", messages.Count);

        foreach (var message in messages)
        {
            if (ct.IsCancellationRequested) break;
            await ProcessMessageAsync(message.Id!, message.Body?.Content ?? string.Empty, ct);
        }
    }

    private async Task ProcessMessageAsync(string messageId, string body, CancellationToken ct)
    {
        _logger.LogInformation("Verarbeite Nachricht {Id}", messageId);

        var customerData = await _aiService.ExtractCustomerInfoAsync(body, ct);

        if (customerData is null || !customerData.IsValid())
        {
            _logger.LogWarning("KI konnte keine validen Daten aus Nachricht {Id} extrahieren — wird in Fehler-Ordner verschoben", messageId);
            await _graphService.MoveToErrorFolderAsync(messageId, ct);
            return;
        }

        _logger.LogInformation("Extrahiert: {Company} / {First} {Last} <{Email}>",
            customerData.CompanyName, customerData.FirstName, customerData.LastName, customerData.Email);

        var created = await _bexioService.CreateContactIfNotExistsAsync(customerData, ct);

        if (created)
            await _graphService.MarkAsReadAsync(messageId, ct);
        else
            await _graphService.MarkAsReadAsync(messageId, ct); // Auch bei Duplikat als gelesen markieren
    }
}
