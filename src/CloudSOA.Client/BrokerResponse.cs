namespace CloudSOA.Client;

/// <summary>
/// Typed broker response — compatible with HPC Pack BrokerResponse&lt;T&gt;.
/// Accessing Result when IsFault is true throws an exception, matching HPC Pack behavior.
/// </summary>
public class BrokerResponse<T> where T : class
{
    private T? _result;

    /// <summary>
    /// The deserialized response message.
    /// Throws if IsFault is true, matching HPC Pack behavior so clients don't need explicit IsFault checks.
    /// </summary>
    public T? Result
    {
        get
        {
            if (IsFault)
                throw new InvalidOperationException(
                    $"Cannot access Result of a faulted response: {FaultMessage}");
            return _result;
        }
        set => _result = value;
    }

    /// <summary>User-defined correlation data.</summary>
    public string? UserData { get; set; }

    /// <summary>Whether this response represents a fault/error.</summary>
    public bool IsFault { get; set; }

    /// <summary>Fault message if IsFault is true.</summary>
    public string? FaultMessage { get; set; }
}
