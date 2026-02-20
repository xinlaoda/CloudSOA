using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DurableProcessingService
{
    public class DurableProcessorImpl : IDurableProcessor
    {
        /// <summary>
        /// Simulates long-running data processing: hash computation + sorting.
        /// Each call takes ~100-500ms depending on input size.
        /// </summary>
        public string ProcessData(string taskId, string inputData)
        {
            var startTime = DateTime.UtcNow;

            // Simulate CPU work: repeated SHA256 hashing
            byte[] data = Encoding.UTF8.GetBytes(inputData ?? "default");
            using (var sha256 = SHA256.Create())
            {
                for (int i = 0; i < 5000; i++)
                {
                    data = sha256.ComputeHash(data);
                }
            }

            // Generate a deterministic result based on input
            var resultHash = BitConverter.ToString(data).Replace("-", "").Substring(0, 16);
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

            return JsonSerializer.Serialize(new
            {
                taskId = taskId,
                status = "completed",
                resultHash = resultHash,
                inputLength = (inputData ?? "").Length,
                processingTimeMs = Math.Round(elapsed, 1),
                processedAt = DateTime.UtcNow.ToString("o"),
                hostName = Environment.MachineName
            });
        }

        /// <summary>
        /// Computes aggregate statistics: mean, stddev, min, max, median, percentiles.
        /// </summary>
        public string ComputeStatistics(string datasetJson)
        {
            double[] values;
            try
            {
                values = JsonSerializer.Deserialize<double[]>(datasetJson);
            }
            catch
            {
                // Generate dataset from hash of input
                var rng = new Random(datasetJson?.GetHashCode() ?? 0);
                values = Enumerable.Range(0, 10000).Select(_ => rng.NextDouble() * 1000).ToArray();
            }

            if (values == null || values.Length == 0)
                values = new double[] { 0 };

            var sorted = values.OrderBy(v => v).ToArray();
            var n = sorted.Length;
            var mean = sorted.Average();
            var variance = sorted.Sum(v => (v - mean) * (v - mean)) / n;
            var stddev = Math.Sqrt(variance);

            return JsonSerializer.Serialize(new
            {
                count = n,
                mean = Math.Round(mean, 4),
                stddev = Math.Round(stddev, 4),
                min = sorted[0],
                max = sorted[n - 1],
                median = sorted[n / 2],
                p25 = sorted[n / 4],
                p75 = sorted[3 * n / 4],
                p95 = sorted[(int)(n * 0.95)],
                p99 = sorted[(int)(n * 0.99)],
                hostName = Environment.MachineName
            });
        }

        /// <summary>
        /// Simulates data transformation with repeated encode/decode cycles.
        /// </summary>
        public string TransformData(string input, int iterations)
        {
            if (iterations <= 0) iterations = 100;
            if (iterations > 10000) iterations = 10000;

            var data = input ?? "sample-data";
            for (int i = 0; i < iterations; i++)
            {
                // Base64 encode → SHA256 → hex → repeat
                var bytes = Encoding.UTF8.GetBytes(data);
                var b64 = Convert.ToBase64String(bytes);
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(b64));
                    data = BitConverter.ToString(hash).Replace("-", "");
                }
            }

            return JsonSerializer.Serialize(new
            {
                result = data.Substring(0, Math.Min(data.Length, 32)),
                iterations = iterations,
                inputLength = (input ?? "").Length,
                hostName = Environment.MachineName
            });
        }
    }
}
