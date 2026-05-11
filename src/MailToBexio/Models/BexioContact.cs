using System.Text.Json.Serialization;

namespace MailToBexio.Models;

public class BexioContact
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    // 1 = Firma, 2 = Kontaktperson
    [JsonPropertyName("contact_type_id")]
    public int ContactTypeId { get; set; }

    [JsonPropertyName("name_1")]
    public string? Name1 { get; set; }

    [JsonPropertyName("name_2")]
    public string? Name2 { get; set; }

    [JsonPropertyName("mail")]
    public string? Mail { get; set; }

    [JsonPropertyName("phone_fixed")]
    public string? PhoneFixed { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("postcode")]
    public string? Postcode { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("country_id")]
    public int? CountryId { get; set; }

    // Referenz auf übergeordnete Firma (bei Kontaktpersonen)
    [JsonPropertyName("contact_group_ids")]
    public List<int>? ContactGroupIds { get; set; }
}

public class BexioSearchRequest
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("criteria")]
    public string Criteria { get; set; } = "=";
}
