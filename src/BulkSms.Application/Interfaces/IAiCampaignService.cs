using BulkSms.Domain.Models;

namespace BulkSms.Application.Interfaces;

/// <summary>
/// Turns a campaign description into reviewed SMS suggestions. It never sends SMS.
/// </summary>
public interface IAiCampaignService
{
    Task<CampaignDraftResponse> CreateDraftAsync(
        CampaignDraftRequest request,
        string userKey,
        CancellationToken cancellationToken = default);
}
