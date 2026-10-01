using BulkSms.Domain.Models;

namespace BulkSms.Application.Interfaces;

public interface IMobileNumberValidator
{
    NumberValidationItem Validate(string? raw);
    ValidateNumbersResult ValidateMany(IEnumerable<string?> rawNumbers);
}
