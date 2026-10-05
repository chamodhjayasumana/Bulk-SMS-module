using BulkSms.Application.Ai;
using BulkSms.Application.Interfaces;
using BulkSms.Application.Providers;
using BulkSms.Application.Services;
using BulkSms.Domain.Models;
using BulkSms.Domain.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BulkSms.Tests;

public class AiCampaignServiceTests
{
    [Fact]
    public async Task MockProvider_ReturnsDeterministicSuggestions()
    {
        var service = Create(new MockAiProvider());
        var result = await service.CreateDraftAsync(Sample(), "user-1");

        Assert.Equal(3, result.Suggestions.Count);
        Assert.True(result.RequiresReview);
        Assert.All(result.Suggestions, item =>
        {
            Assert.Equal("en", item.Language);
            Assert.Equal("professional", item.Tone);
            Assert.Contains("weekend discount", item.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("{name}", item.Message, StringComparison.Ordinal);
            Assert.Contains("My Shop", item.Message, StringComparison.Ordinal);
            Assert.True(item.CharacterCount > 0);
            Assert.True(item.Segments >= 1);
            Assert.DoesNotContain(item.Warnings, w => w.Contains("not allowed", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public async Task UnsupportedLanguage_IsRejected()
    {
        var provider = new FixedProvider("Hi");
        var service = Create(provider);
        var request = Sample();
        request.Language = "fr";

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateDraftAsync(request, "user"));
        Assert.Contains("not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task EmptyDescription_IsRejected()
    {
        var service = Create(new FixedProvider("Hi"));
        var request = Sample();
        request.CampaignDescription = "   ";

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateDraftAsync(request, "user"));
        Assert.Contains("required", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PromptLength_IsRejected()
    {
        var service = Create(new FixedProvider("Hi"), new AiOptions { MaxPromptLength = 20 });
        var request = Sample();
        request.CampaignDescription = new string('a', 21);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateDraftAsync(request, "user"));
        Assert.Contains("characters", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestionCount_IsClampedToTheHardMaximum()
    {
        var provider = new FixedProvider("One", "Two", "Three", "Four", "Five", "Six");
        var service = Create(provider, new AiOptions { MaxSuggestions = 99, HardMaxSuggestions = 5 });

        var result = await service.CreateDraftAsync(Sample(), "user");

        Assert.Equal(5, provider.LastCount);
        Assert.Equal(5, result.Suggestions.Count);
    }

    [Fact]
    public async Task LongMessage_AddsSegmentWarning()
    {
        var service = Create(new FixedProvider(new string('a', 200)));
        var request = Sample();
        request.MaxSegments = 1;
        request.PersonalizationFields.Clear();

        var result = await service.CreateDraftAsync(request, "user");

        Assert.Single(result.Suggestions);
        Assert.True(result.Suggestions[0].Segments > 1);
        Assert.Contains(result.Suggestions[0].Warnings, w => w.Contains("maximum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnsupportedPlaceholder_IsWarned()
    {
        var service = Create(new FixedProvider("Hello {secretField}"));
        var result = await service.CreateDraftAsync(Sample(), "user");

        Assert.Contains(result.Suggestions[0].Warnings, w => w.Contains("{secretField}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AllowedPlaceholder_IsAccepted()
    {
        var service = Create(new FixedProvider("Hello {name}, order {orderNumber} is ready."));
        var request = Sample();
        request.PersonalizationFields = new List<string> { "name", "orderNumber" };

        var result = await service.CreateDraftAsync(request, "user");

        Assert.DoesNotContain(result.Suggestions[0].Warnings, w => w.Contains("not allowed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("name", result.Suggestions[0].Placeholders);
        Assert.Contains("orderNumber", result.Suggestions[0].Placeholders);
    }

    [Fact]
    public async Task SevereSuggestion_IsBlockedAndSafeSuggestionRemains()
    {
        var service = Create(new FixedProvider("Shop opens at 9.", "Your password is hunter2"));
        var result = await service.CreateDraftAsync(Sample(), "user");

        Assert.Single(result.Suggestions);
        Assert.Equal("Shop opens at 9.", result.Suggestions[0].Message);
        Assert.Contains(result.SafetyWarnings, w => w.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SevereDescription_DoesNotCallProvider()
    {
        var provider = new FixedProvider("Hi");
        var service = Create(provider);
        var request = Sample();
        request.CampaignDescription = "Your OTP is 123456";

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateDraftAsync(request, "user"));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task DisabledExternalProvider_IsRejected()
    {
        var provider = new NamedProvider("OpenAI");
        var service = Create(provider, new AiOptions { Enabled = false, Provider = "OpenAI" });

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateDraftAsync(Sample(), "user"));
        Assert.Contains("disabled", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task RequestGuard_BlocksTheExtraCall()
    {
        var service = Create(
            new FixedProvider("Hello there"),
            new AiOptions { RequestsPerMinute = 8 },
            new AiRequestGuard());
        var optionsUser = "rate-user";
        await service.CreateDraftAsync(Sample(), optionsUser);
        await service.CreateDraftAsync(Sample(), optionsUser);
        await service.CreateDraftAsync(Sample(), optionsUser);
        await service.CreateDraftAsync(Sample(), optionsUser);
        await service.CreateDraftAsync(Sample(), optionsUser);
        await service.CreateDraftAsync(Sample(), optionsUser);
        await service.CreateDraftAsync(Sample(), optionsUser);
        await service.CreateDraftAsync(Sample(), optionsUser);

        await Assert.ThrowsAsync<AiRateLimitException>(() => service.CreateDraftAsync(Sample(), optionsUser));
    }

    private static CampaignDraftRequest Sample() => new()
    {
        CampaignDescription = "Promote our weekend discount",
        Language = "en",
        SenderName = "My Shop",
        MaxSegments = 2,
        Tone = "professional",
        IncludeCallToAction = true,
        PersonalizationFields = new List<string> { "name" }
    };

    private static AiCampaignService Create(IAiProvider provider, AiOptions? options = null, IAiRequestGuard? guard = null)
    {
        options ??= new AiOptions { RequestsPerMinute = 100 };
        return new AiCampaignService(
            provider,
            new ContentSafetyService(new SmsSegmentCalculator()),
            guard ?? new AllowGuard(),
            Options.Create(options),
            NullLogger<AiCampaignService>.Instance);
    }

    private sealed class AllowGuard : IAiRequestGuard
    {
        public void Check(string userKey, int limitPerMinute)
        {
        }
    }

    private sealed class FixedProvider : IAiProvider
    {
        private readonly string[] _messages;
        public int Calls { get; private set; }
        public int LastCount { get; private set; }
        public string Name => "Mock";

        public FixedProvider(params string[] messages) => _messages = messages;

        public Task<IReadOnlyList<string>> GenerateAsync(AiGenerationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastCount = request.SuggestionCount;
            return Task.FromResult<IReadOnlyList<string>>(_messages.Take(request.SuggestionCount).ToArray());
        }
    }

    private sealed class NamedProvider : IAiProvider
    {
        public NamedProvider(string name) => Name = name;
        public string Name { get; }
        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> GenerateAsync(AiGenerationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<string>>(new[] { "Hello" });
        }
    }
}
