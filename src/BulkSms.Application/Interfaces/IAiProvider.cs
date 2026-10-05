using BulkSms.Domain.Models;

namespace BulkSms.Application.Interfaces;

/// <summary>
/// Generates SMS copy only. Implementations must not send SMS or log message text and secrets.
/// </summary>
public interface IAiProvider
{
    /// <summary>Mock or OpenAI.</summary>
    string Name { get; }

    Task<IReadOnlyList<string>> GenerateAsync(AiGenerationRequest request, CancellationToken cancellationToken = default);
}
