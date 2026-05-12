using System.Net.Http.Json;
using System.Text.Json;
using MailToBexio.Configuration;
using MailToBexio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailToBexio.Services.AI;

// OpenAI-compatible API from Ollama.
public class OllamaService : IAIService
{
    private const string Prompt = """
        Extrahiere aus dieser E-Mail die Kontaktdaten und gib ausschliesslich ein valides JSON-Objekt zurueck.
        Kein Markdown, kein Codeblock, kein erklaerender Text.
        Nutze auch Betreff und Absender, wenn der Body nur wenig Informationen enthaelt.
                Trenne Organisation und Person strikt:
                - companyName nur fuer echte Organisationen, Firmen, Institutionen oder Teams.
                - firstName/lastName nur fuer eine klar erkennbare Kontaktperson.
                - Ziehe einen Namen aus einer Signatur nicht automatisch als Person heran, wenn er als Absender-/Footer-Block ohne klare Personenkontext erscheint.
                - Wenn ein Mail-Footer eine Organisation enthaelt und daneben eine Personensignatur steht, gib beides separat zurueck.
                - Wenn nur die Organisation erkennbar ist, lasse firstName und lastName auf null.
                - Wenn nur die Person erkennbar ist, lasse companyName auf null.
                Bevorzuge fuer companyName nur eindeutige Organisationshinweise wie Rechtsformen, Institutionen, Abteilungen oder Firmenbezeichnungen.
        Wenn ein Feld nicht erkennbar ist, setze es auf null.
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
                new { role = "system", content = Prompt },
                new { role = "user", content = mailBody }
            },
            format = "json",
            options = new { temperature = 0 },
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

            var parsed = ParseJson(text);
            if (parsed is not null)
            {
                _logger.LogInformation("Ollama extrahiert: Company={Company}, First={First}, Last={Last}, Email={Email}",
                    parsed.CompanyName, parsed.FirstName, parsed.LastName, parsed.Email);
            }

            return parsed;
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
            return JsonSerializer.Deserialize<CustomerData>(ExtractJsonObject(json), options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KI hat kein valides JSON geliefert: {Json}", json);
            return null;
        }
    }

    private static string ExtractJsonObject(string json)
    {
        var trimmed = json.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');

        return start >= 0 && end > start
            ? trimmed[start..(end + 1)]
            : trimmed;
    }
}
