namespace BulkSms.Application.Interfaces;

public interface IRecipientFileParser
{
    Task<IReadOnlyList<string>> ParseAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default);
}
