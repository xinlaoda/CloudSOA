using System.Reflection;

namespace CloudSOA.ServiceHost.Wcf.Loader;

/// <summary>
/// Metadata for a discovered WCF [OperationContract] method.
/// </summary>
public class WcfMethodInfo
{
    /// <summary>Action name (from OperationContract.Name or method name).</summary>
    public required string ActionName { get; init; }

    /// <summary>The reflected method to invoke.</summary>
    public required MethodInfo Method { get; init; }

    /// <summary>Parameter types for deserialization.</summary>
    public required Type[] ParameterTypes { get; init; }

    /// <summary>Return type of the method.</summary>
    public required Type ReturnType { get; init; }
}
