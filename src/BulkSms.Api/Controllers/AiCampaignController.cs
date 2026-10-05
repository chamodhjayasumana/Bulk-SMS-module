using System.Security.Claims;
using BulkSms.Api.Auth;
using BulkSms.Application.Ai;
using BulkSms.Application.Interfaces;
using BulkSms.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace BulkSms.Api.Controllers;

/// <summary>Creates SMS copy for a person to review. This controller never sends SMS.</summary>
[ApiController]
[Route("api/ai")]
[BulkSmsAuthorize]
public class AiCampaignController : ControllerBase
{
    private readonly IAiCampaignService _campaigns;
    private readonly ILogger<AiCampaignController> _logger;

    public AiCampaignController(IAiCampaignService campaigns, ILogger<AiCampaignController> logger)
    {
        _campaigns = campaigns;
        _logger = logger;
    }

    /// <summary>Returns message suggestions. The user must still confirm any later send.</summary>
    [HttpPost("campaign-draft")]
    [RequestSizeLimit(32_768)]
    public async Task<ActionResult<ApiResponse<CampaignDraftResponse>>> Draft(
        [FromBody] CampaignDraftRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var draft = await _campaigns.CreateDraftAsync(request, UserKey(), cancellationToken);
            return Ok(ApiResponse<CampaignDraftResponse>.Ok(draft, "Review these suggestions before sending."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<CampaignDraftResponse>.Fail(ex.Message));
        }
        catch (AiRateLimitException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, ApiResponse<CampaignDraftResponse>.Fail(ex.Message));
        }
        catch (AiProviderException ex) when (ex.IsTimeout)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, ApiResponse<CampaignDraftResponse>.Fail(ex.Message));
        }
        catch (AiProviderException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ApiResponse<CampaignDraftResponse>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI campaign draft failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<CampaignDraftResponse>.Fail("The campaign draft could not be created."));
        }
    }

    private string UserKey() =>
        User.FindFirstValue(ClaimTypes.Name)
        ?? User.FindFirstValue("id")
        ?? "unknown";
}
