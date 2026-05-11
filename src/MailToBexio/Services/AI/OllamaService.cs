using System.Net.Http.Json;
using System.Text.Json;
using MailToBexio.Configuration;
using MailToBexio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailToBexio.Services.AI;

// OpenAI-kompatible API von Ollama
public class OllamaService : IAIService
{
    private const string Prompt = """
        Extrahiere aus dieser E-Mail die Kontaktdaten und gib sie ausschliesslich als valides JSON zurück.
        Kein Markdown, kein Codeblock, nur reines JSON.
        Schema:
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
        E-Mail-Inhalt:
        """;

    private readonly HttpClient _http;
    private readonly AiSettings _settings;
    private readonly ILogger<OllamaService> _logger;

    public OllamaService(IHttpClientFactory httpFactory, IOptions<AiSettings> settings, ILogger<OllamaService> logger)
    {
        _http = httpFactory.CreateClient("Ollama");
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<CustomerData?> ExtractCustomerInfoAsync(string mailBody, CancellationToken ct = default)
    {
        var requestBody = new
        {
            model = _settings.OllamaModel,
            messages = new[]
            {
                new { role = "user", content = Prompt + "\n\n" + mailBody }
            },
            stream = false
        };

        var response = await _http.PostAsJsonAsync("/api/chat", requestBody, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Ollama API Fehler: {Status}", response.StatusCode);
            return null;
        }

        var raw = await response.Content.ReadAsStringAsync(ct);

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var text = doc.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return ParseJson(text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Parsen der Ollama-Antwort: {Raw}", raw);
            return null;
        }
    }

    private CustomerData? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<CustomerData>(json.Trim(), options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KI hat kein valides JSON geliefert: {Json}", json);
            return null;
        }
    }
}
