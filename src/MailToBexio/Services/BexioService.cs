using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using MailToBexio.Configuration;
using MailToBexio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailToBexio.Services;

public interface IBexioService
{
    Task<BexioContactResult> CreateContactIfNotExistsAsync(CustomerData data, CancellationToken ct = default);
}

public enum BexioContactResult
{
    Created,
    AlreadyExists,
    Failed
}

public class BexioService : IBexioService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex StreetRegex = new("^(?<street>.+?)\\s+(?<house>[0-9][0-9a-zA-Z\\-/]*)$", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly ILogger<BexioService> _logger;
    private readonly BexioSettings _settings;

    public BexioService(IHttpClientFactory httpFactory, IOptions<BexioSettings> settings, ILogger<BexioService> logger)
    {
        _http = httpFactory.CreateClient("Bexio");
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<BexioContactResult> CreateContactIfNotExistsAsync(CustomerData data, CancellationToken ct = default)
    {
        // Stufe 1: E-Mail-Adresse
        if (!string.IsNullOrWhiteSpace(data.Email))
        {
            var byEmail = await SearchContactAsync("mail", data.Email, ct);
            if (byEmail.Count > 0)
            {
                _logger.LogInformation("Kontakt mit E-Mail {Email} existiert bereits (ID {Id}) — übersprungen",
                    data.Email, byEmail[0].Id);
                return BexioContactResult.AlreadyExists;
            }
        }

        int? existingCompanyId = null;

        // Stufe 2: Firmenname
        if (!string.IsNullOrWhiteSpace(data.CompanyName))
        {
            var byCompany = await SearchContactAsync("name_1", data.CompanyName, ct);
            existingCompanyId = byCompany.FirstOrDefault(c => c.ContactTypeId == 1)?.Id;

            if (existingCompanyId.HasValue)
                _logger.LogInformation("Firma '{Name}' bereits vorhanden (ID {Id}) — Kontaktperson wird verknüpft",
                    data.CompanyName, existingCompanyId);
        }

        // Stufe 3: Personenname (nur wenn kein Firmentreffer)
        if (existingCompanyId is null && !string.IsNullOrWhiteSpace(data.LastName))
        {
            var personName = string.IsNullOrWhiteSpace(data.FirstName)
                ? data.LastName
                : $"{data.LastName} {data.FirstName}";

            var byPerson = await SearchContactAsync("name_1", personName, ct);
            if (byPerson.Count > 0)
            {
                _logger.LogInformation("Person '{Name}' existiert bereits (ID {Id}) — übersprungen",
                    personName, byPerson[0].Id);
                return BexioContactResult.AlreadyExists;
            }
        }

        // Kein Treffer: Firma anlegen (falls vorhanden), dann Kontaktperson
        if (!string.IsNullOrWhiteSpace(data.CompanyName) && existingCompanyId is null)
        {
            var newCompany = BuildContact(data, isCompany: true, parentId: null);
            existingCompanyId = await PostContactAsync(newCompany, ct);
            if (existingCompanyId is null) return BexioContactResult.Failed;

            _logger.LogInformation("Firma '{Name}' angelegt (ID {Id})", data.CompanyName, existingCompanyId);
        }

        var hasPersonName = !string.IsNullOrWhiteSpace(data.LastName) || !string.IsNullOrWhiteSpace(data.FirstName);

        if (!hasPersonName)
        {
            _logger.LogInformation(
                "Keine Kontaktperson-Daten vorhanden für Nachricht; nur Firma wird verarbeitet");
            return BexioContactResult.Created;
        }

        // Kontaktperson anlegen
        var contact = BuildContact(data, isCompany: false, parentId: existingCompanyId);
        var contactId = await PostContactAsync(contact, ct);
        if (contactId is null) return BexioContactResult.Failed;

        _logger.LogInformation("Kontaktperson '{First} {Last}' angelegt (ID {Id})",
            data.FirstName, data.LastName, contactId);
        return BexioContactResult.Created;
    }

    private async Task<List<BexioContact>> SearchContactAsync(string field, string value, CancellationToken ct)
    {
        var body = new[] { new BexioSearchRequest { Field = field, Value = Sanitize(value) ?? string.Empty } };

        try
        {
            var response = await _http.PostAsJsonAsync("contact/search", body, ct);
            if (!response.IsSuccessStatusCode) return [];

            var result = await response.Content.ReadFromJsonAsync<List<BexioContact>>(JsonOpts, ct);
            return result ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei bexio Suche nach {Field}={Value}", field, value);
            return [];
        }
    }

    private async Task<int?> PostContactAsync(BexioContact contact, CancellationToken ct)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("contact", contact, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("bexio POST /contact Fehler {Status}: {Body}", response.StatusCode, error);
                return null;
            }

            var created = await response.Content.ReadFromJsonAsync<BexioContact>(JsonOpts, ct);
            return created?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Anlegen des Kontakts in bexio");
            return null;
        }
    }

    private BexioContact BuildContact(CustomerData data, bool isCompany, int? parentId)
    {
        var street = SplitStreet(Sanitize(data.Street));

        return new BexioContact
        {
            ContactTypeId = isCompany ? 1 : 2,
            Name1 = Sanitize(isCompany ? data.CompanyName : data.LastName),
            Name2 = isCompany ? null : Sanitize(data.FirstName),
            Mail = Sanitize(data.Email),
            PhoneFixed = Sanitize(data.Phone),
            StreetName = street.streetName,
            HouseNumber = street.houseNumber,
            Postcode = Sanitize(data.Zip),
            City = Sanitize(data.City),
            ContactGroupIds = parentId.HasValue ? [parentId.Value] : null,
            UserId = _settings.UserId,
            OwnerId = _settings.OwnerId
        };
    }

    private static (string? streetName, string? houseNumber) SplitStreet(string? street)
    {
        if (string.IsNullOrWhiteSpace(street))
            return (null, null);

        var match = StreetRegex.Match(street);
        if (!match.Success)
            return (street, null);

        return (Sanitize(match.Groups["street"].Value), Sanitize(match.Groups["house"].Value));
    }

    // Entfernt Steuerzeichen und begrenzt Länge — verhindert Injection in die bexio API
    internal static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > 200 ? clean[..200] : clean;
    }
}
