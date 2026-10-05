using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BulkSms.Application.Interfaces;
using BulkSms.Domain.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BulkSms.Tests;

public class AiCampaignApiTests : IClassFixture<AiCampaignApiTests.AiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly AiFactory _factory;

    public AiCampaignApiTests(AiFactory factory) => _factory = factory;

    [Fact]
    public async Task Draft_RequiresAuthentication()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/ai/campaign-draft", Body());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Draft_WithMockProvider_ReturnsSuggestionsAndRequiresReview()
    {
        var client = await AuthorizedClient();
        var response = await client.PostAsJsonAsync("/api/ai/campaign-draft", Body());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<CampaignDraftResponse>>(JsonOptions);
        Assert.True(payload!.Success);
        Assert.True(payload.Data!.RequiresReview);
        Assert.Equal(3, payload.Data.Suggestions.Count);
        Assert.Contains("weekend discount", payload.Data.Suggestions[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Draft_RejectsUnsupportedLanguageAndEmptyDescriptionAndLongPrompt()
    {
        var client = await AuthorizedClient();

        var language = await client.PostAsJsonAsync("/api/ai/campaign-draft", Body(language: "fr"));
        Assert.Equal(HttpStatusCode.BadRequest, language.StatusCode);

        var empty = await client.PostAsJsonAsync("/api/ai/campaign-draft", Body(description: " "));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var longPrompt = await client.PostAsJsonAsync("/api/ai/campaign-draft", Body(description: new string('a', 2001)));
        Assert.Equal(HttpStatusCode.BadRequest, longPrompt.StatusCode);
    }

    [Fact]
    public async Task Draft_DoesNotCallSmsProvider()
    {
        var counter = new CountingSmsProvider();
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISmsProvider>();
                services.AddSingleton(counter);
                services.AddSingleton<ISmsProvider>(sp => sp.GetRequiredService<CountingSmsProvider>());
            });
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Token(client));

        var response = await client.PostAsJsonAsync("/api/ai/campaign-draft", Body());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, counter.Calls);
    }

    [Fact]
    public async Task Draft_DoesNotReturnConfiguredApiKey()
    {
        var client = await AuthorizedClient();
        var response = await client.PostAsJsonAsync("/api/ai/campaign-draft", Body());
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sk-test-DO-NOT-LEAK", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Draft_RateLimit_Returns429()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("AI:Provider", "Mock");
            builder.UseSetting("AI:RequestsPerMinute", "2");
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Token(client));

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/ai/campaign-draft", Body())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/ai/campaign-draft", Body())).StatusCode);
        var limited = await client.PostAsJsonAsync("/api/ai/campaign-draft", Body());
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    private async Task<HttpClient> AuthorizedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Token(client));
        return client;
    }

    private static async Task<string> Token(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/Auth/token", new { username = "bulksms.admin", password = "ChangeMe123!" });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        return json!.Data!.GetProperty("token").GetString()!;
    }

    private static object Body(string description = "Promote our weekend discount", string language = "en") => new
    {
        campaignDescription = description,
        language,
        senderName = "My Shop",
        maxSegments = 2,
        tone = "professional",
        includeCallToAction = true,
        personalizationFields = new[] { "name" }
    };

    private sealed class CountingSmsProvider : ISmsProvider
    {
        public int Calls { get; private set; }

        public Task<ProviderSendResult> SendAsync(string mobileNumber, string message, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ProviderSendResult { Success = true });
        }
    }

    public sealed class AiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("AI:Provider", "Mock");
            builder.UseSetting("AI:Enabled", "false");
            builder.UseSetting("AI:ApiKey", "sk-test-DO-NOT-LEAK");
            builder.UseSetting("AI:RequestsPerMinute", "100");
        }
    }
}
