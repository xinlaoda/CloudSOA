namespace CloudSOA.Client;

/// <summary>
/// Typed broker response — compatible with HPC Pack BrokerResponse&lt;T&gt;.
/// </summary>
public class BrokerResponse<T> where T : class
{
    /// <summary>The deserialized response message.</summary>
    public T? Result { get; set; }

    /// <summary>User-defined correlation data.</summary>
    public string? UserData { get; set; }

    /// <summary>Whether this response represents a fault/error.</summary>
    public bool IsFault { get; set; }

    /// <summary>Fault message if IsFault is true.</summary>
    public string? FaultMessage { get; set; }
}
