namespace BulkSms.Application.Ai;

/// <summary>The signed-in user asked for drafts too quickly.</summary>
public sealed class AiRateLimitException : Exception
{
    public AiRateLimitException(string message) : base(message)
    {
    }
}

/// <summary>The external AI call failed. Messages are safe to return to the client and must not contain secrets.</summary>
public sealed class AiProviderException : Exception
{
    public AiProviderException(string message, bool isTimeout = false, bool isTransient = false) : base(message)
    {
        IsTimeout = isTimeout;
        IsTransient = isTransient;
    }

    public bool IsTimeout { get; }

    public bool IsTransient { get; }
}
