using System.Runtime.Serialization;

namespace CalculatorClient;

// These message contracts mirror what "Add Service Reference" generates in HPC Pack SOA.
// Each operation has a Request and Response pair.

[DataContract(Namespace = "http://tempuri.org/")]
public class AddRequest
{
    [DataMember(Order = 0)] public double a { get; set; }
    [DataMember(Order = 1)] public double b { get; set; }
    public AddRequest() { }
    public AddRequest(double a, double b) { this.a = a; this.b = b; }
}

[DataContract(Namespace = "http://tempuri.org/")]
public class AddResponse
{
    [DataMember(Order = 0)] public double AddResult { get; set; }
}

[DataContract(Namespace = "http://tempuri.org/")]
public class SubtractRequest
{
    [DataMember(Order = 0)] public double a { get; set; }
    [DataMember(Order = 1)] public double b { get; set; }
    public SubtractRequest() { }
    public SubtractRequest(double a, double b) { this.a = a; this.b = b; }
}

[DataContract(Namespace = "http://tempuri.org/")]
public class SubtractResponse
{
    [DataMember(Order = 0)] public double SubtractResult { get; set; }
}

[DataContract(Namespace = "http://tempuri.org/")]
public class MultiplyRequest
{
    [DataMember(Order = 0)] public double a { get; set; }
    [DataMember(Order = 1)] public double b { get; set; }
    public MultiplyRequest() { }
    public MultiplyRequest(double a, double b) { this.a = a; this.b = b; }
}

[DataContract(Namespace = "http://tempuri.org/")]
public class MultiplyResponse
{
    [DataMember(Order = 0)] public double MultiplyResult { get; set; }
}

[DataContract(Namespace = "http://tempuri.org/")]
public class EchoRequest
{
    [DataMember(Order = 0)] public string message { get; set; } = string.Empty;
    public EchoRequest() { }
    public EchoRequest(string message) { this.message = message; }
}

[DataContract(Namespace = "http://tempuri.org/")]
public class EchoResponse
{
    [DataMember(Order = 0)] public string EchoResult { get; set; } = string.Empty;
}
