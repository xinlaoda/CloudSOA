using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CloudSOA.Client
{
    /// <summary>
    /// HPC Pack-compatible Session class for .NET Framework 4.8.
    /// </summary>
    public class Session : IDisposable
    {
        private readonly CloudSession _inner;

        private Session(CloudSession inner)
        {
            _inner = inner;
        }

        public string Id { get { return _inner.SessionId; } }
        internal string BrokerEndpoint { get { return _inner.BrokerEndpoint; } }
        internal CloudSession InnerSession { get { return _inner; } }

        /// <summary>
        /// Create a new session — compatible with HPC Pack Session.CreateSession.
        /// </summary>
        public static Session CreateSession(SessionStartInfo info)
        {
            return CreateSessionAsync(info).GetAwaiter().GetResult();
        }

        public static async Task<Session> CreateSessionAsync(
            SessionStartInfo info, CancellationToken ct = default(CancellationToken))
        {
            var inner = await CloudSession.CreateAsync(info.HeadNode, info.ServiceName, ct);
            return new Session(inner);
        }

        public void Close()
        {
            _inner.CloseAsync().GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            try { Close(); } catch { }
        }
    }

    /// <summary>
    /// HPC Pack-compatible SessionStartInfo.
    /// </summary>
    public class SessionStartInfo
    {
        public string HeadNode { get; private set; }
        public string ServiceName { get; private set; }

        public SessionStartInfo(string headNode, string serviceName)
        {
            HeadNode = headNode;
            ServiceName = serviceName;
        }

        // HPC Pack compatibility — ignored in CloudSOA
        public string Username { get; set; }
        public string Password { get; set; }
        public bool Secure { get; set; }
    }

    /// <summary>
    /// Internal cloud session implementation.
    /// </summary>
    public class CloudSession
    {
        public string SessionId { get; private set; }
        public string BrokerEndpoint { get; private set; }
        private HttpClient _http;

        private CloudSession() { }

        public static async Task<CloudSession> CreateAsync(
            string brokerUrl, string serviceName, CancellationToken ct = default(CancellationToken))
        {
            var baseUrl = brokerUrl.TrimEnd('/');
            var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

            var body = JsonConvert.SerializeObject(new { serviceName = serviceName });
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await http.PostAsync("/api/v1/sessions", content, ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync();
            var result = JObject.Parse(json);

            return new CloudSession
            {
                SessionId = result.Value<string>("sessionId"),
                BrokerEndpoint = baseUrl,
                _http = http
            };
        }

        public async Task CloseAsync(CancellationToken ct = default(CancellationToken))
        {
            try
            {
                await _http.DeleteAsync("/api/v1/sessions/" + SessionId, ct);
            }
            catch { }
            _http.Dispose();
        }
    }
}
