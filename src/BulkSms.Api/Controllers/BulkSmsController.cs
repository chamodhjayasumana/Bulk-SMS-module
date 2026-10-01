using BulkSms.Api.Auth;
using BulkSms.Application.Interfaces;
using BulkSms.Domain.Enums;
using BulkSms.Domain.Models;
using BulkSms.Domain.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BulkSms.Api.Controllers;

[ApiController]
[Route("api/sms")]
[BulkSmsAuthorize]
public class BulkSmsController : ControllerBase
{
    private readonly IBulkSmsService _bulkSmsService;
    private readonly IAndroidSmsGateway _androidGateway;
    private readonly SmsProviderOptions _options;
    private readonly SmsGatewayOptions _gatewayOptions;
    private readonly ILogger<BulkSmsController> _logger;

    public BulkSmsController(
        IBulkSmsService bulkSmsService,
        IAndroidSmsGateway androidGateway,
        IOptions<SmsProviderOptions> options,
        IOptions<SmsGatewayOptions> gatewayOptions,
        ILogger<BulkSmsController> logger)
    {
        _bulkSmsService = bulkSmsService;
        _androidGateway = androidGateway;
        _options = options.Value;
        _gatewayOptions = gatewayOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Asks the local Android gateway whether it is online. Does not send an SMS.
    /// </summary>
    [HttpGet("gateway/status")]
    public async Task<ActionResult<ApiResponse<SmsGatewayStatus>>> GatewayStatus(CancellationToken cancellationToken)
    {
        var mode = ActiveMode();
        try
        {
            var status = await _androidGateway.GetStatusAsync(cancellationToken);
            ApplyMode(status, mode);
            var message = status.Online
                ? "Gateway online."
                : status.Error ?? "Android gateway is offline.";
            if (!string.Equals(mode, "Android", StringComparison.OrdinalIgnoreCase) && !status.Online)
                message = $"SMS mode is {mode}. No real SMS will be sent.";
            return Ok(ApiResponse<SmsGatewayStatus>.Ok(status, message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gateway status check failed");
            var status = new SmsGatewayStatus
            {
                Online = false,
                Error = "Android gateway is offline."
            };
            ApplyMode(status, mode);
            return Ok(ApiResponse<SmsGatewayStatus>.Ok(status, "Android gateway is offline."));
        }
    }

    /// <summary>
    /// Sends one SMS through the active provider. Mock mode does not use the phone.
    /// </summary>
    [HttpPost("send-test")]
    public async Task<ActionResult<ApiResponse<SmsRecipientResult>>> SendTest(
        [FromBody] TestSmsRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PhoneNumber))
            return BadRequest(ApiResponse<SmsRecipientResult>.Fail("Phone number is required."));

        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(ApiResponse<SmsRecipientResult>.Fail("Message is required."));

        try
        {
            var result = await _bulkSmsService.SendBulkAsync(new BulkSendRequest
            {
                Message = request.Message,
                Recipients = new List<string> { request.PhoneNumber },
                Confirmed = true,
                IsTest = true
            }, cancellationToken);

            var item = result.Results.FirstOrDefault();
            if (item is null)
                return BadRequest(ApiResponse<SmsRecipientResult>.Fail("No valid recipients after validation."));

            var message = item.Status == SmsDeliveryStatus.Sent
                ? "Test SMS accepted."
                : item.ErrorMessage ?? "Test SMS failed.";
            return Ok(ApiResponse<SmsRecipientResult>.Ok(item, message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<SmsRecipientResult>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test SMS failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<SmsRecipientResult>.Fail("Test SMS failed."));
        }
    }

    /// <summary>
    /// Validate uploaded CSV/XLSX recipient file.
    /// Optional form field: message (for segment estimate).
    /// </summary>
    [HttpPost("validate")]
    [RequestSizeLimit(10_000_000)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<ValidateNumbersResult>>> Validate(
        IFormFile file,
        [FromForm] string? message,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<ValidateNumbersResult>.Fail("File is required."));

        if (file.Length > _options.MaxUploadBytes)
            return BadRequest(ApiResponse<ValidateNumbersResult>.Fail("Uploaded file exceeds maximum allowed size."));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".csv" or ".xlsx"))
            return BadRequest(ApiResponse<ValidateNumbersResult>.Fail("Only .csv and .xlsx files are supported."));

        try
        {
            await using var stream = file.OpenReadStream();
            // Copy to memory so parsers that need seek (CSV header detection / ClosedXML) work reliably
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;

            var result = await _bulkSmsService.ValidateFileAsync(ms, file.FileName, message, cancellationToken);
            return Ok(ApiResponse<ValidateNumbersResult>.Ok(result, "Validation completed."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ValidateNumbersResult>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<ValidateNumbersResult>.Fail("Validation failed."));
        }
    }

    /// <summary>
    /// Send the same message to a pre-validated recipient list.
    /// </summary>
    [HttpPost("send-bulk")]
    public async Task<ActionResult<ApiResponse<BulkSendResult>>> SendBulk(
        [FromBody] BulkSendRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _bulkSmsService.SendBulkAsync(request, cancellationToken);
            return Ok(ApiResponse<BulkSendResult>.Ok(result, "Bulk send completed."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<BulkSendResult>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            // Duplicate submit / confirmation required
            return Conflict(ApiResponse<BulkSendResult>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk send failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<BulkSendResult>.Fail("Bulk send failed."));
        }
    }

    [HttpPost("estimate")]
    public ActionResult<ApiResponse<MessageEstimate>> Estimate([FromBody] EstimateRequest request)
    {
        if (request is null)
            return BadRequest(ApiResponse<MessageEstimate>.Fail("Request is required."));

        var validated = _bulkSmsService.ValidateRecipients(request.Recipients ?? new List<string>(), request.Message);
        return Ok(ApiResponse<MessageEstimate>.Ok(validated.Estimate));
    }

    public record EstimateRequest(string? Message, List<string>? Recipients);

    public record TestSmsRequest(string? PhoneNumber, string? Message);

    private string ActiveMode()
    {
        if (string.Equals(_gatewayOptions.Provider, "Android", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_options.Provider, "Android", StringComparison.OrdinalIgnoreCase))
            return "Android";

        if (string.Equals(_options.Provider, "Rest", StringComparison.OrdinalIgnoreCase))
            return "Rest";

        return "Mock";
    }

    private static void ApplyMode(SmsGatewayStatus status, string mode)
    {
        status.Mode = mode;
        status.SendingAllowed = !string.Equals(mode, "Android", StringComparison.OrdinalIgnoreCase) || status.Online;
    }
}
