using System;

namespace HeavyComputeService
{
    public class HeavyComputeServiceImpl : IHeavyCompute
    {
        public double EstimatePi(int iterations)
        {
            if (iterations <= 0) iterations = 100000;
            var rng = new Random(42);
            int inside = 0;
            for (int i = 0; i < iterations; i++)
            {
                double x = rng.NextDouble();
                double y = rng.NextDouble();
                if (x * x + y * y <= 1.0)
                    inside++;
            }
            return 4.0 * inside / iterations;
        }

        public double MatrixMultiply(int size)
        {
            if (size <= 0 || size > 500) size = 100;
            var rng = new Random(42);
            var a = new double[size, size];
            var b = new double[size, size];
            var c = new double[size, size];

            for (int i = 0; i < size; i++)
                for (int j = 0; j < size; j++)
                {
                    a[i, j] = rng.NextDouble();
                    b[i, j] = rng.NextDouble();
                }

            // O(n^3) matrix multiplication
            for (int i = 0; i < size; i++)
                for (int j = 0; j < size; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < size; k++)
                        sum += a[i, k] * b[k, j];
                    c[i, j] = sum;
                }

            // Return checksum
            double checksum = 0;
            for (int i = 0; i < size; i++)
                for (int j = 0; j < size; j++)
                    checksum += c[i, j];
            return checksum;
        }

        public long ComputeNthPrime(int n)
        {
            if (n <= 0) n = 1000;
            int count = 0;
            long candidate = 1;
            while (count < n)
            {
                candidate++;
                if (IsPrime(candidate))
                    count++;
            }
            return candidate;
        }

        private static bool IsPrime(long number)
        {
            if (number < 2) return false;
            if (number < 4) return true;
            if (number % 2 == 0 || number % 3 == 0) return false;
            for (long i = 5; i * i <= number; i += 6)
            {
                if (number % i == 0 || number % (i + 2) == 0)
                    return false;
            }
            return true;
        }
    }
}
