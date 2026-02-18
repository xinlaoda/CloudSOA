namespace CloudSOA.Common.Exceptions;

public class SessionNotFoundException : Exception
{
    public string SessionId { get; }

    public SessionNotFoundException(string sessionId)
        : base($"Session '{sessionId}' not found.")
    {
        SessionId = sessionId;
    }
}

public class SessionStateException : Exception
{
    public string SessionId { get; }

    public SessionStateException(string sessionId, string message)
        : base(message)
    {
        SessionId = sessionId;
    }
}
