namespace IrisSort.Services.Exceptions;

/// <summary>
/// Exception thrown when LM Studio API requests fail.
/// </summary>
public class LmStudioApiException : Exception
{
    /// <summary>
    /// HTTP status code if available.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Creates a new exception with a message.
    /// </summary>
    public LmStudioApiException(string message) : base(message) { }

    /// <summary>
    /// Creates a new exception with a message and status code.
    /// </summary>
    public LmStudioApiException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Creates a new exception with a message and inner exception.
    /// </summary>
    public LmStudioApiException(string message, Exception innerException)
        : base(message, innerException) { }
}
