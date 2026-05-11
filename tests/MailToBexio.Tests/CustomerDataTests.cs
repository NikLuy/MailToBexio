using MailToBexio.Models;

namespace MailToBexio.Tests;

public class CustomerDataTests
{
    [Fact]
    public void IsValid_WithEmailAndLastName_ReturnsTrue()
    {
        var data = new CustomerData { Email = "max@example.com", LastName = "Muster" };
        Assert.True(data.IsValid());
    }

    [Fact]
    public void IsValid_WithEmailAndCompanyName_ReturnsTrue()
    {
        var data = new CustomerData { Email = "info@firma.ch", CompanyName = "Muster AG" };
        Assert.True(data.IsValid());
    }

    [Fact]
    public void IsValid_MissingEmail_ReturnsFalse()
    {
        var data = new CustomerData { LastName = "Muster", CompanyName = "Muster AG" };
        Assert.False(data.IsValid());
    }

    [Fact]
    public void IsValid_EmptyEmail_ReturnsFalse()
    {
        var data = new CustomerData { Email = "   ", LastName = "Muster" };
        Assert.False(data.IsValid());
    }

    [Fact]
    public void IsValid_EmailOnly_NoNameOrCompany_ReturnsFalse()
    {
        var data = new CustomerData { Email = "info@example.com" };
        Assert.False(data.IsValid());
    }

    [Fact]
    public void IsValid_AllFieldsNull_ReturnsFalse()
    {
        var data = new CustomerData();
        Assert.False(data.IsValid());
    }
}
