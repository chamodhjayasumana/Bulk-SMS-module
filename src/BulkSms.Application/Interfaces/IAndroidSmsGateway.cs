using BulkSms.Domain.Models;

namespace BulkSms.Application.Interfaces;

public interface IAndroidSmsGateway
{
    Task<SmsGatewayStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<ProviderSendResult> SendAsync(
        string mobileNumber,
        string message,
        string requestId,
        CancellationToken cancellationToken = default);
}
