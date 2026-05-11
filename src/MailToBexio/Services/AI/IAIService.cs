using MailToBexio.Models;

namespace MailToBexio.Services.AI;

public interface IAIService
{
    Task<CustomerData?> ExtractCustomerInfoAsync(string mailBody, CancellationToken ct = default);
}
