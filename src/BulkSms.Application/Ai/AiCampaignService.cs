using System.Text.RegularExpressions;
using BulkSms.Application.Interfaces;
using BulkSms.Domain.Models;
using BulkSms.Domain.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkSms.Application.Ai;

/// <summary>
/// Validates a campaign description, asks the configured AI provider for copy, then checks each suggestion.
/// This service does not send SMS.
/// </summary>
public sealed class AiCampaignService : IAiCampaignService
{
    private static readonly HashSet<string> Languages = new(StringComparer.OrdinalIgnoreCase) { "en", "si", "ta" };
    private static readonly HashSet<string> Tones = new(StringComparer.OrdinalIgnoreCase)
    {
        "professional", "friendly", "urgent", "promotional"
    };
    private static readonly Regex FieldName = new(@"^[A-Za-z][A-Za-z0-9_]{0,31}$", RegexOptions.Compiled);

    private readonly IAiProvider _provider;
    private readonly IContentSafetyService _safety;
    private readonly IAiRequestGuard _guard;
    private readonly AiOptions _options;
    private readonly ILogger<AiCampaignService> _logger;

    public AiCampaignService(
        IAiProvider provider,
        IContentSafetyService safety,
        IAiRequestGuard guard,
        IOptions<AiOptions> options,
        ILogger<AiCampaignService> logger)
    {
        _provider = provider;
        _safety = safety;
        _guard = guard;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CampaignDraftResponse> CreateDraftAsync(
        CampaignDraftRequest request,
        string userKey,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentException("Campaign description is required.");

        var description = (request.CampaignDescription ?? string.Empty).Trim();
        if (description.Length == 0)
            throw new ArgumentException("Campaign description is required.");

        var maxPrompt = Math.Clamp(_options.MaxPromptLength, 20, 8000);
        if (description.Length > maxPrompt)
            throw new ArgumentException($"Campaign description must be {maxPrompt} characters or fewer.");

        var language = (request.Language ?? string.Empty).Trim().ToLowerInvariant();
        if (!Languages.Contains(language))
            throw new ArgumentException("Language is not supported. Use en, si, or ta.");

        var tone = (request.Tone ?? string.Empty).Trim().ToLowerInvariant();
        if (!Tones.Contains(tone))
            throw new ArgumentException("Tone must be professional, friendly, urgent, or promotional.");

        if (request.MaxSegments is < 1 or > 5)
            throw new ArgumentException("Maximum SMS segments must be from 1 to 5.");

        var sender = (request.SenderName ?? string.Empty).Trim();
        if (sender.Length > 40)
            throw new ArgumentException("Sender name must be 40 characters or fewer.");

        var placeholders = NormalizeFields(request.PersonalizationFields);
        if (!_provider.Name.Equals("Mock", StringComparison.OrdinalIgnoreCase) && !_options.Enabled)
            throw new ArgumentException("The external AI provider is disabled.");

        if (_safety.IsSeverelyUnsafe(description))
            throw new ArgumentException("The campaign description was blocked by a safety rule.");

        _guard.Check(userKey, _options.RequestsPerMinute);

        var suggestionCount = Math.Clamp(
            _options.MaxSuggestions <= 0 ? 3 : _options.MaxSuggestions,
            1,
            Math.Clamp(_options.HardMaxSuggestions, 1, 5));

        var generated = await _provider.GenerateAsync(new AiGenerationRequest
        {
            CampaignDescription = description,
            Language = language,
            SenderName = sender,
            Tone = tone,
            IncludeCallToAction = request.IncludeCallToAction,
            AllowedPlaceholders = placeholders,
            SuggestionCount = suggestionCount,
            MaxSegments = request.MaxSegments
        }, cancellationToken);

        var response = new CampaignDraftResponse { RequiresReview = true };
        foreach (var raw in generated.Take(suggestionCount))
        {
            var report = _safety.InspectMessage(raw, placeholders, request.MaxSegments);
            if (report.Findings.Any(f => f.Code == "empty"))
            {
                response.SafetyWarnings.Add("One suggestion was empty and was removed.");
                continue;
            }

            if (_options.BlockSevereFindings && report.HasSevere)
            {
                response.SafetyWarnings.Add(report.Findings.First(f => f.Severe).Message);
                continue;
            }

            response.Suggestions.Add(new CampaignSuggestion
            {
                Message = raw.Trim(),
                Language = language,
                CharacterCount = report.CharacterCount,
                Segments = report.Segments,
                Tone = tone,
                Warnings = report.Findings.Select(f => f.Message).Distinct().ToList(),
                Placeholders = report.Placeholders
            });
            foreach (var finding in report.Findings)
                response.SafetyWarnings.Add(finding.Message);
        }

        response.SafetyWarnings = response.SafetyWarnings.Distinct().ToList();
        if (response.Suggestions.Count == 0)
            throw new ArgumentException("Every suggestion was blocked by a safety rule.");

        _logger.LogInformation(
            "AI draft created. Provider={Provider} Language={Language} Tone={Tone} Suggestions={SuggestionCount} Warnings={WarningCount}",
            _provider.Name,
            language,
            tone,
            response.Suggestions.Count,
            response.SafetyWarnings.Count);
        return response;
    }

    private static IReadOnlyList<string> NormalizeFields(IEnumerable<string>? fields)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields ?? Array.Empty<string>())
        {
            var name = (field ?? string.Empty).Trim();
            if (name.Length == 0)
                continue;
            if (!FieldName.IsMatch(name))
                throw new ArgumentException("A personalization field name is not allowed.");
            if (seen.Add(name))
                list.Add(name);
            if (list.Count > 8)
                throw new ArgumentException("Too many personalization fields.");
        }
        return list;
    }
}
