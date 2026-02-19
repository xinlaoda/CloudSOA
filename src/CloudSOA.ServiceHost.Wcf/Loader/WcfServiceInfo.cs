namespace CloudSOA.ServiceHost.Wcf.Loader;

/// <summary>
/// Describes a WCF service type discovered from a DLL.
/// </summary>
public class WcfServiceInfo
{
    /// <summary>Service name from [ServiceContract(Name=...)] or type name.</summary>
    public required string ServiceName { get; init; }

    /// <summary>The concrete service implementation type.</summary>
    public required Type ImplementationType { get; init; }

    /// <summary>The service contract interface type.</summary>
    public required Type ContractType { get; init; }

    /// <summary>Discovered operation methods.</summary>
    public required IReadOnlyList<WcfMethodInfo> Methods { get; init; }
}
