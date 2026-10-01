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

    /// <summary>Single test send. Skips the short batch duplicate guard so a test can be repeated.</summary>
    public bool IsTest { get; set; }
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
    public string? RequestId { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public class SmsGatewayStatus
{
    public bool Online { get; set; }
    public bool NetworkAvailable { get; set; }
    public string SimOperator { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public int? Port { get; set; }
    public string? Error { get; set; }

    /// <summary>Mock, Rest, or Android. Comes from ASP.NET configuration, not from the phone.</summary>
    public string Mode { get; set; } = "Mock";

    /// <summary>False only when real Android sending is selected and the phone gateway is offline.</summary>
    public bool SendingAllowed { get; set; } = true;
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
