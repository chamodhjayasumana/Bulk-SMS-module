using BulkSms.Domain.Enums;

namespace BulkSms.Domain.Models;

public class ValidateNumbersResult
{
    public int Total { get; set; }
    public int Valid { get; set; }
    public int Invalid { get; set; }
    public int Duplicates { get; set; }
    public List<string> Numbers { get; set; } = new();
    public List<InvalidNumberSample> InvalidSamples { get; set; } = new();
    public MessageEstimate Estimate { get; set; } = new();
}

public class InvalidNumberSample
{
    public string RawValue { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class MessageEstimate
{
    public int CharacterCount { get; set; }
    public int SegmentCount { get; set; }
    public bool IsUnicode { get; set; }
    public int RecipientCount { get; set; }
    public int EstimatedTotalSmsCount { get; set; }
}

public class BulkSendRequest
{
    public string Message { get; set; } = string.Empty;
    public List<string> Recipients { get; set; } = new();
    public bool Confirmed { get; set; }
}

public class BulkSendResult
{
    public int Total { get; set; }
    public int Successful { get; set; }
    public int Failed { get; set; }
    public List<SmsRecipientResult> Results { get; set; } = new();
}

public class SmsRecipientResult
{
    public string MobileNumber { get; set; } = string.Empty;
    public SmsDeliveryStatus Status { get; set; } = SmsDeliveryStatus.Pending;
    public string? ProviderMessageId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public class ProviderSendResult
{
    public bool Success { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsTransientFailure { get; set; }
    public int? HttpStatusCode { get; set; }
}

public class NumberValidationItem
{
    public string RawValue { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string? Normalized { get; set; }
    public string? Reason { get; set; }
    public bool IsDuplicate { get; set; }
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "OK") =>
        new() { Success = true, Message = message, Data = data };

    public static ApiResponse<T> Fail(string message) =>
        new() { Success = false, Message = message };
}
