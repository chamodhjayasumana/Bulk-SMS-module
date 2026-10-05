using BulkSms.Domain.Models;

namespace BulkSms.Application.Interfaces;

/// <summary>
/// Checks campaign text for length, placeholders, and safety issues without logging the text.
/// </summary>
public interface IContentSafetyService
{
    SafetyReport InspectMessage(string? message, IReadOnlyCollection<string> allowedPlaceholders, int maxSegments);

    /// <summary>True when the campaign description itself must not be sent to a provider.</summary>
    bool IsSeverelyUnsafe(string? text);
}
