using System.ServiceModel;

namespace HeavyComputeService
{
    [ServiceContract]
    public interface IHeavyCompute
    {
        /// <summary>Monte Carlo Pi estimation with N iterations.</summary>
        [OperationContract]
        double EstimatePi(int iterations);

        /// <summary>Multiply two NxN matrices filled with random values, return checksum.</summary>
        [OperationContract]
        double MatrixMultiply(int size);

        /// <summary>Compute Nth prime number using trial division.</summary>
        [OperationContract]
        long ComputeNthPrime(int n);
    }
}
