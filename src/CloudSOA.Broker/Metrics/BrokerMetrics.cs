using Prometheus;

namespace CloudSOA.Broker.Metrics;

/// <summary>
/// Prometheus 指标定义 — 全局单例
/// </summary>
public static class BrokerMetrics
{
    private static readonly string[] SessionLabels = { "service_name" };
    private static readonly string[] RequestLabels = { "session_id", "action" };

    // Session 指标
    public static readonly Gauge ActiveSessions = Prometheus.Metrics
        .CreateGauge("cloudsoa_sessions_active", "Number of active sessions");

    public static readonly Counter SessionsCreated = Prometheus.Metrics
        .CreateCounter("cloudsoa_sessions_created_total", "Total sessions created", SessionLabels);

    public static readonly Counter SessionsClosed = Prometheus.Metrics
        .CreateCounter("cloudsoa_sessions_closed_total", "Total sessions closed");

    // Request 指标
    public static readonly Counter RequestsEnqueued = Prometheus.Metrics
        .CreateCounter("cloudsoa_requests_enqueued_total", "Total requests enqueued", RequestLabels);

    public static readonly Counter RequestsProcessed = Prometheus.Metrics
        .CreateCounter("cloudsoa_requests_processed_total", "Total requests processed");

    public static readonly Counter RequestsFailed = Prometheus.Metrics
        .CreateCounter("cloudsoa_requests_failed_total", "Total requests failed");

    public static readonly Counter RequestsDeadLettered = Prometheus.Metrics
        .CreateCounter("cloudsoa_requests_deadlettered_total", "Total dead-lettered requests");

    // Response 指标
    public static readonly Counter ResponsesDelivered = Prometheus.Metrics
        .CreateCounter("cloudsoa_responses_delivered_total", "Total responses delivered to clients");

    // Queue 指标
    public static readonly Gauge QueueDepth = Prometheus.Metrics
        .CreateGauge("cloudsoa_queue_depth", "Current queue depth", new[] { "session_id" });

    // Latency 指标
    public static readonly Histogram RequestLatency = Prometheus.Metrics
        .CreateHistogram("cloudsoa_request_duration_seconds", "Request processing duration",
            new HistogramConfiguration
            {
                Buckets = Histogram.ExponentialBuckets(0.001, 2, 15) // 1ms to ~16s
            });

    // Dispatcher 指标
    public static readonly Gauge ActiveDispatchers = Prometheus.Metrics
        .CreateGauge("cloudsoa_dispatchers_active", "Number of active dispatcher loops");
}
