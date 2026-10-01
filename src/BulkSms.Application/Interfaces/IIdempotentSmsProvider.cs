using BulkSms.Domain.Models;

namespace BulkSms.Application.Interfaces;

/// <summary>
/// Provider that accepts a caller-supplied request id so retries reuse the same id.
/// </summary>
public interface IIdempotentSmsProvider : ISmsProvider
{
    Task<ProviderSendResult> SendAsync(
        string mobileNumber,
        string message,
        string requestId,
        CancellationToken cancellationToken = default);
}
