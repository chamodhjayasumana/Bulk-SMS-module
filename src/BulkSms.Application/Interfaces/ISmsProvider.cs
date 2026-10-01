using BulkSms.Domain.Models;

namespace BulkSms.Application.Interfaces;

public interface ISmsProvider
{
    Task<ProviderSendResult> SendAsync(string mobileNumber, string message, CancellationToken cancellationToken = default);
}
