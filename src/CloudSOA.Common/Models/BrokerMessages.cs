namespace CloudSOA.Common.Models;

/// <summary>
/// SOA 请求消息，从客户端发送到 Broker 队列
/// </summary>
public class BrokerRequest
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    public string? UserData { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// SOA 响应消息，从 Service Host 返回给 Broker
/// </summary>
public class BrokerResponse
{
    public string MessageId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string RequestMessageId { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    public string? UserData { get; set; }
    public bool IsFault { get; set; }
    public string? FaultMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
