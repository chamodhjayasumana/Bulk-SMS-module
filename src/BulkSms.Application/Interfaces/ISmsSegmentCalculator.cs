using BulkSms.Domain.Models;

namespace BulkSms.Application.Interfaces;

public interface ISmsSegmentCalculator
{
    MessageEstimate Estimate(string message, int recipientCount);
}
