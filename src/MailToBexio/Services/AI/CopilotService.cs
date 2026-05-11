using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using MailToBexio.Configuration;
using MailToBexio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace MailToBexio.Services.AI;

// Azure OpenAI (Copilot-Backend) — App-Key Auth, kein User-Login nötig
public class CopilotService : IAIService
{
    private const string SystemPrompt = """
        Du bist ein Daten-Extraktions-Assistent.
        Extrahiere aus E-Mail-Texten die Kontaktdaten und antworte ausschliesslich mit validem JSON.
        Kein Markdown, kein Codeblock, nur reines JSON-Objekt.
        """;

    private const string UserPromptTemplate = """
        Extrahiere die Kontaktdaten aus dieser E-Mail und gib sie als JSON zurück:
        {
          "companyName": "string oder null",
          "firstName": "string oder null",
          "lastName": "string oder null",
          "email": "string oder null",
          "phone": "string oder null",
          "street": "string oder null",
          "zip": "string oder null",
          "city": "string oder null",
          "country": "string oder null"
        }

        E-Mail:
        {MAIL_BODY}
        """;

    private readonly ChatClient _chatClient;
    private readonly ILogger<CopilotService> _logger;

    public CopilotService(IOptions<AiSettings> settings, ILogger<CopilotService> logger)
    {
        _logger = logger;
        var s = settings.Value;

        var azureClient = new AzureOpenAIClient(
            new Uri(s.CopilotEndpoint),
            new AzureKeyCredential(s.CopilotApiKey));

        _chatClient = azureClient.GetChatClient(s.CopilotDeploymentName);
    }

    public async Task<CustomerData?> ExtractCustomerInfoAsync(string mailBody, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(UserPromptTemplate.Replace("{MAIL_BODY}", mailBody))
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0,  // deterministische Ausgabe für strukturierte Extraktion
            MaxOutputTokenCount = 512
        };

        try
        {
            var response = await _chatClient.CompleteChatAsync(messages, options, ct);
            var text = response.Value.Content[0].Text;
            return ParseJson(text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Aufruf der Azure OpenAI API");
            return null;
        }
    }

    private CustomerData? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<CustomerData>(json.Trim(), opts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KI hat kein valides JSON geliefert: {Json}", json);
            return null;
        }
    }
}
