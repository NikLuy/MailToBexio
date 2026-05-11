using System.Net;
using System.Net.Http.Headers;
using MailToBexio.Configuration;
using MailToBexio.Models;
using MailToBexio.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RichardSzalay.MockHttp;

namespace MailToBexio.Tests;

public class BexioServiceTests
{
    // Baut einen BexioService mit einem MockHttpMessageHandler
    private static (BexioService service, MockHttpMessageHandler mock) BuildService()
    {
        var mockHttp = new MockHttpMessageHandler();
        var client = mockHttp.ToHttpClient();
        client.BaseAddress = new Uri("https://api.bexio.com/2.0/");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Bexio").Returns(client);

        var service = new BexioService(
            factory,
            Options.Create(new BexioSettings()),
            NullLogger<BexioService>.Instance);

        return (service, mockHttp);
    }

    private static CustomerData ValidCustomer() => new()
    {
        CompanyName = "Muster AG",
        FirstName = "Max",
        LastName = "Muster",
        Email = "max@muster.ch",
        Street = "Bahnhofstrasse 1",
        Zip = "8001",
        City = "Zürich"
    };

    [Fact]
    public async Task CreateContact_EmailAlreadyExists_ReturnsFalse()
    {
        var (svc, mock) = BuildService();

        // Stufe 1: E-Mail-Suche liefert Treffer → überspringen
        mock.When(HttpMethod.Post, "*/contact/search")
            .Respond("application/json", """[{"id":1,"contact_type_id":2,"mail":"max@muster.ch"}]""");

        var result = await svc.CreateContactIfNotExistsAsync(ValidCustomer());

        Assert.False(result);
    }

    [Fact]
    public async Task CreateContact_CompanyExists_CreatesContactPersonOnly()
    {
        var (svc, mock) = BuildService();

        mock.When(HttpMethod.Post, "*/contact/search")
            .Respond(async req =>
            {
                var body = await req.Content!.ReadAsStringAsync();
                // Stufe 1: E-Mail → kein Treffer
                if (body.Contains("\"mail\""))
                    return JsonResponse("[]");
                // Stufe 2: Firmenname → Firma gefunden
                if (body.Contains("Muster AG"))
                    return JsonResponse("""[{"id":42,"contact_type_id":1,"name_1":"Muster AG"}]""");
                return JsonResponse("[]");
            });

        var postCount = 0;
        mock.When(HttpMethod.Post, "*/contact")
            .Respond(_ =>
            {
                postCount++;
                return Task.FromResult(JsonResponse("""{"id":99,"contact_type_id":2}"""));
            });

        var result = await svc.CreateContactIfNotExistsAsync(ValidCustomer());

        Assert.True(result);
        // Nur ein POST /contact (Kontaktperson — Firma existiert bereits, wird nicht neu angelegt)
        Assert.Equal(1, postCount);
    }

    [Fact]
    public async Task CreateContact_PersonAlreadyExists_ReturnsFalse()
    {
        var (svc, mock) = BuildService();
        var data = new CustomerData
        {
            FirstName = "Max",
            LastName = "Muster",
            Email = "max@muster.ch"
            // Kein CompanyName → Stufe 3 greift
        };

        mock.When(HttpMethod.Post, "*/contact/search")
            .Respond(async req =>
            {
                var body = await req.Content!.ReadAsStringAsync();
                // Stufe 1: E-Mail → kein Treffer
                if (body.Contains("\"mail\""))
                    return JsonResponse("[]");
                // Stufe 3: Personenname → Treffer
                return JsonResponse("""[{"id":7,"contact_type_id":2,"name_1":"Muster"}]""");
            });

        var result = await svc.CreateContactIfNotExistsAsync(data);

        Assert.False(result);
    }

    [Fact]
    public async Task CreateContact_NoMatchAnywhere_CreatesFirmaAndKontaktperson()
    {
        var (svc, mock) = BuildService();

        mock.When(HttpMethod.Post, "*/contact/search")
            .Respond("application/json", "[]");

        var postCount = 0;
        mock.When(HttpMethod.Post, "*/contact")
            .Respond(_ =>
            {
                postCount++;
                return Task.FromResult(JsonResponse($"{{\"id\":{postCount * 10},\"contact_type_id\":{(postCount == 1 ? 1 : 2)}}}"));
            });

        var result = await svc.CreateContactIfNotExistsAsync(ValidCustomer());

        Assert.True(result);
        Assert.Equal(2, postCount); // Firma + Kontaktperson
    }

    [Theory]
    [InlineData("Normal Text", "Normal Text")]
    [InlineData("Firma\x00\x1F AG", "Firma AG")]
    [InlineData("  Leerzeichen  ", "Leerzeichen")]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    public void Sanitize_RemovesControlCharsAndTrims(string? input, string? expected)
    {
        Assert.Equal(expected, BexioService.Sanitize(input));
    }

    [Fact]
    public void Sanitize_LongString_TruncatesAt200()
    {
        var longInput = new string('A', 250);
        var result = BexioService.Sanitize(longInput);
        Assert.Equal(200, result!.Length);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
}
