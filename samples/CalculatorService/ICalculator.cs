using System.ServiceModel;

namespace CalculatorService
{
    [ServiceContract]
    public interface ICalculator
    {
        [OperationContract]
        double Add(double a, double b);

        [OperationContract]
        double Subtract(double a, double b);

        [OperationContract]
        double Multiply(double a, double b);

        [OperationContract]
        double Divide(double a, double b);

        [OperationContract]
        string Echo(string message);
    }
}
