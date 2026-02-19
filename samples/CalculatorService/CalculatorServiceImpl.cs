namespace CalculatorService
{
    public class CalculatorServiceImpl : ICalculator
    {
        public double Add(double a, double b) => a + b;
        public double Subtract(double a, double b) => a - b;
        public double Multiply(double a, double b) => a * b;
        public double Divide(double a, double b) => b == 0
            ? throw new System.DivideByZeroException("Cannot divide by zero")
            : a / b;
        public string Echo(string message) => $"Echo: {message} [processed at {System.DateTime.UtcNow:O}]";
    }
}
