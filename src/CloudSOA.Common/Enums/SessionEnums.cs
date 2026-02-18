namespace CloudSOA.Common.Enums;

public enum SessionState
{
    Creating = 0,
    Active = 1,
    Closing = 2,
    Closed = 3,
    Faulted = 4,
    TimedOut = 5
}

public enum TransportScheme
{
    Grpc = 0,
    Http = 1,
    WebSocket = 2
}

public enum SessionType
{
    Interactive = 0,
    Durable = 1
}
