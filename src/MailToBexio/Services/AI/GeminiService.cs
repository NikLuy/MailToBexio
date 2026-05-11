using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailToBexio.Configuration;
using MailToBexio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailToBexio.Services.AI;

public class GeminiService : IAIService
{
    private const string ApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent";
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
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(IHttpClientFactory httpFactory, IOptions<AiSettings> settings, ILogger<GeminiService> logger)
    {
        _http = httpFactory.CreateClient("Gemini");
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<CustomerData?> ExtractCustomerInfoAsync(string mailBody, CancellationToken ct = default)
    {
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = Prompt + "\n\n" + mailBody } }
                }
            }
        };

        var url = $"{ApiUrl}?key={_settings.GeminiApiKey}";
        var response = await _http.PostAsJsonAsync(url, requestBody, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini API Fehler: {Status}", response.StatusCode);
            return null;
        }

        var raw = await response.Content.ReadAsStringAsync(ct);

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return ParseJson(text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Parsen der Gemini-Antwort: {Raw}", raw);
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
