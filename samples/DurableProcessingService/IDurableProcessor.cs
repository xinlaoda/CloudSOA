using System.ServiceModel;

namespace DurableProcessingService
{
    [ServiceContract(Name = "IDurableProcessor", Namespace = "http://cloudsoa.samples/durable")]
    public interface IDurableProcessor
    {
        /// <summary>
        /// Simulate a long-running data processing task.
        /// Returns processing result with task ID for tracking.
        /// </summary>
        [OperationContract]
        string ProcessData(string taskId, string inputData);

        /// <summary>
        /// Compute aggregate statistics over a dataset (CPU-intensive).
        /// </summary>
        [OperationContract]
        string ComputeStatistics(string datasetJson);

        /// <summary>
        /// Simulate file transformation (encode/decode cycle).
        /// </summary>
        [OperationContract]
        string TransformData(string input, int iterations);
    }
}
