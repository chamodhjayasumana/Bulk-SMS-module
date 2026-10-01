using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BulkSms.Domain.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BulkSms.Tests;

public class BulkSmsApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly WebApplicationFactory<Program> _factory;

    public BulkSmsApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    private async Task<string> GetTokenAsync(HttpClient client, string user = "bulksms.admin", string pass = "ChangeMe123!")
    {
        var response = await client.PostAsJsonAsync("/api/Auth/token", new { username = user, password = pass });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        return json!.Data.GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task Validate_RequiresAuthorization()
    {
        var client = _factory.CreateClient();
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("Mobile\n0712345678")), "file", "numbers.csv");

        var response = await client.PostAsync("/api/sms/validate", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Validate_WithToken_ReturnsCounts()
    {
        var client = _factory.CreateClient();
        var token = await GetTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var csv = "Mobile\n0712345678\n0771234567\n0712345678\n0112345678\nbad\n";
        var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "numbers.csv" },
            { new StringContent("Hello"), "message" }
        };

        var response = await client.PostAsync("/api/sms/validate", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ValidateNumbersResult>>(JsonOptions);
        Assert.True(payload!.Success);
        Assert.Equal(5, payload.Data!.Total);
        Assert.Equal(2, payload.Data.Valid);
        Assert.Equal(1, payload.Data.Duplicates);
        Assert.Equal(2, payload.Data.Invalid);
    }

    [Fact]
    public async Task SendBulk_UnauthorizedWithoutToken()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/sms/send-bulk", new
        {
            message = "Hi",
            recipients = new[] { "94771234567" },
            confirmed = true
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SendBulk_SucceedsWithMockProvider()
    {
        var client = _factory.CreateClient();
        var token = await GetTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/sms/send-bulk", new
        {
            message = "Hello API " + Guid.NewGuid(),
            recipients = new[] { "0712345671", "0712345670" },
            confirmed = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<BulkSendResult>>(JsonOptions);
        Assert.True(payload!.Success);
        Assert.Equal(2, payload.Data!.Total);
        Assert.Equal(1, payload.Data.Successful);
        Assert.Equal(1, payload.Data.Failed);
    }

    [Fact]
    public async Task Token_RejectsBadPassword()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/token", new { username = "bulksms.admin", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
