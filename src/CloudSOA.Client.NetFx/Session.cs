using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
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
            var inner = await CloudSession.CreateAsync(info, ct);
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

        // HPC Pack compatibility
        public string Username { get; set; }
        public string Password { get; set; }
        public bool Secure { get; set; }

        /// <summary>Accept self-signed or untrusted server certificates (development only).</summary>
        public bool AcceptUntrustedCertificates { get; set; }

        /// <summary>Client certificate for mutual TLS authentication.</summary>
        public X509Certificate2 ClientCertificate { get; set; }
    }

    /// <summary>
    /// Internal cloud session implementation.
    /// </summary>
    public class CloudSession
    {
        public string SessionId { get; private set; }
        public string BrokerEndpoint { get; private set; }
        internal HttpClient Http { get; private set; }

        private CloudSession() { }

        internal static HttpClient CreateHttpClient(SessionStartInfo info)
        {
            var handler = new HttpClientHandler();

            // Accept self-signed certs
            if (info != null && info.AcceptUntrustedCertificates)
            {
                handler.ServerCertificateCustomValidationCallback =
                    delegate { return true; };
            }

            // Mutual TLS: client certificate
            if (info != null && info.ClientCertificate != null)
            {
                handler.ClientCertificates.Add(info.ClientCertificate);
            }

            // Ensure TLS 1.2+
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

            return new HttpClient(handler);
        }

        public static async Task<CloudSession> CreateAsync(
            SessionStartInfo info, CancellationToken ct = default(CancellationToken))
        {
            var baseUrl = info.HeadNode.TrimEnd('/');
            var http = CreateHttpClient(info);
            http.BaseAddress = new Uri(baseUrl);

            var body = JsonConvert.SerializeObject(new { serviceName = info.ServiceName });
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await http.PostAsync("/api/v1/sessions", content, ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync();
            var result = JObject.Parse(json);

            return new CloudSession
            {
                SessionId = result.Value<string>("sessionId"),
                BrokerEndpoint = baseUrl,
                Http = http
            };
        }

        public async Task CloseAsync(CancellationToken ct = default(CancellationToken))
        {
            try
            {
                await Http.DeleteAsync("/api/v1/sessions/" + SessionId, ct);
            }
            catch { }
            Http.Dispose();
        }
    }
}
