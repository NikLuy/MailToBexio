namespace MailToBexio.Models;

public class CustomerData
{
    public string? CompanyName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Street { get; set; }
    public string? Zip { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Email) &&
        (!string.IsNullOrWhiteSpace(CompanyName) || !string.IsNullOrWhiteSpace(LastName));
}
