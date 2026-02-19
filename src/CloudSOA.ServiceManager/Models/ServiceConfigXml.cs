using System.Xml.Serialization;

namespace CloudSOA.ServiceManager.Models;

/// <summary>
/// XML deserialization model for the service configuration file that users upload
/// alongside the service DLL.
/// </summary>
[XmlRoot("ServiceRegistration", Namespace = "urn:cloudsoa:service-config")]
public class ServiceConfigXml
{
    [XmlElement("ServiceName")]
    public string ServiceName { get; set; } = string.Empty;

    [XmlElement("Version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>"wcf-netfx" | "native-net8"</summary>
    [XmlElement("Runtime")]
    public string Runtime { get; set; } = "native-net8";

    [XmlElement("AssemblyName")]
    public string AssemblyName { get; set; } = string.Empty;

    [XmlElement("ServiceContractType")]
    public string? ServiceContractType { get; set; }

    [XmlElement("Resources")]
    public ServiceResourcesXml Resources { get; set; } = new();

    [XmlArray("Dependencies")]
    [XmlArrayItem("Assembly")]
    public List<string> Dependencies { get; set; } = new();

    [XmlArray("Environment")]
    [XmlArrayItem("Variable")]
    public List<EnvironmentVariableXml> Environment { get; set; } = new();

    /// <summary>
    /// Convert the XML model into the domain <see cref="ServiceRegistration"/> model.
    /// </summary>
    public ServiceRegistration ToRegistration() => new()
    {
        ServiceName = ServiceName,
        Version = Version,
        Runtime = Runtime,
        AssemblyName = AssemblyName,
        ServiceContractType = ServiceContractType,
        Resources = new ServiceResources
        {
            MinInstances = Resources.MinInstances,
            MaxInstances = Resources.MaxInstances,
            CpuPerInstance = Resources.CpuPerInstance,
            MemoryPerInstance = Resources.MemoryPerInstance
        },
        Dependencies = Dependencies,
        Environment = Environment.ToDictionary(e => e.Name, e => e.Value)
    };
}

public class ServiceResourcesXml
{
    [XmlElement("MinInstances")]
    public int MinInstances { get; set; } = 0;

    [XmlElement("MaxInstances")]
    public int MaxInstances { get; set; } = 10;

    [XmlElement("CpuPerInstance")]
    public string CpuPerInstance { get; set; } = "500m";

    [XmlElement("MemoryPerInstance")]
    public string MemoryPerInstance { get; set; } = "512Mi";
}

public class EnvironmentVariableXml
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute("value")]
    public string Value { get; set; } = string.Empty;
}
