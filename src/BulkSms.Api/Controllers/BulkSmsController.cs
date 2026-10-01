using BulkSms.Api.Auth;
using BulkSms.Application.Interfaces;
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
    private readonly SmsProviderOptions _options;
    private readonly ILogger<BulkSmsController> _logger;

    public BulkSmsController(
        IBulkSmsService bulkSmsService,
        IOptions<SmsProviderOptions> options,
        ILogger<BulkSmsController> logger)
    {
        _bulkSmsService = bulkSmsService;
        _options = options.Value;
        _logger = logger;
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
}
