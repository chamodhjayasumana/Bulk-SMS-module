using BulkSms.Domain.Models;

namespace BulkSms.Application.Interfaces;

public interface IBulkSmsService
{
    Task<ValidateNumbersResult> ValidateFileAsync(Stream fileStream, string fileName, string? messagePreview = null, CancellationToken cancellationToken = default);
    ValidateNumbersResult ValidateRecipients(IEnumerable<string?> recipients, string? messagePreview = null);
    Task<BulkSendResult> SendBulkAsync(BulkSendRequest request, CancellationToken cancellationToken = default);
}
