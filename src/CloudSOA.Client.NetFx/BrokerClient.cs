using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CloudSOA.Client
{
    /// <summary>
    /// HPC Pack-compatible BrokerClient&lt;T&gt; for .NET Framework 4.8.
    /// T is the WCF service contract interface (e.g., ICalculator).
    /// </summary>
    public class BrokerClient<T> : IDisposable where T : class
    {
        private readonly HttpClient _http;
        private readonly string _sessionId;
        private readonly List<object> _pendingRequests = new List<object>();
        private int _sentCount;

        public BrokerClient(Session session)
        {
            _sessionId = session.Id;
            _http = new HttpClient { BaseAddress = new Uri(session.BrokerEndpoint.TrimEnd('/')) };
        }

        /// <summary>
        /// Send a typed request. TMessage should be a WCF MessageContract (e.g., AddRequest).
        /// </summary>
        public void SendRequest<TMessage>(TMessage request, string userData = null) where TMessage : class
        {
            var typeName = typeof(TMessage).Name;
            var action = typeName.EndsWith("Request", StringComparison.Ordinal)
                ? typeName.Substring(0, typeName.Length - "Request".Length)
                : typeName;

            var serializer = new DataContractSerializer(typeof(TMessage));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, request);
                var payload = Convert.ToBase64String(ms.ToArray());

                _pendingRequests.Add(new
                {
                    action = action,
                    payload = payload,
                    userData = userData
                });
                _sentCount++;
            }
        }

        /// <summary>Mark that all requests have been sent.</summary>
        public void EndRequests()
        {
            EndRequestsAsync().GetAwaiter().GetResult();
        }

        public async Task EndRequestsAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_pendingRequests.Count == 0) return;

            var json = JsonConvert.SerializeObject(new { requests = _pendingRequests });
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync("/api/v1/sessions/" + _sessionId + "/requests", content, ct);
            resp.EnsureSuccessStatusCode();

            _pendingRequests.Clear();

            // Signal flush
            await _http.PostAsync("/api/v1/sessions/" + _sessionId + "/requests/flush", null, ct);
        }

        /// <summary>
        /// Get typed responses. Compatible with HPC Pack foreach-style enumeration.
        /// </summary>
        public IEnumerable<BrokerResponse<TMessage>> GetResponses<TMessage>() where TMessage : class, new()
        {
            var responses = GetAllResponsesAsync(_sentCount).GetAwaiter().GetResult();
            return DeserializeResponses<TMessage>(responses);
        }

        private async Task<List<JObject>> GetAllResponsesAsync(
            int expectedCount, CancellationToken ct = default(CancellationToken))
        {
            var deadline = DateTime.UtcNow.AddMinutes(5);
            var allResponses = new List<JObject>();

            while (allResponses.Count < expectedCount && DateTime.UtcNow < deadline)
            {
                var url = "/api/v1/sessions/" + _sessionId + "/responses?maxCount=" + (expectedCount - allResponses.Count);
                var resp = await _http.GetStringAsync(url);
                var result = JObject.Parse(resp);
                var items = result["responses"] as JArray;
                if (items != null)
                {
                    foreach (var item in items)
                        allResponses.Add((JObject)item);
                }

                if (allResponses.Count < expectedCount)
                    await Task.Delay(200, ct);
            }

            return allResponses;
        }

        private static IEnumerable<BrokerResponse<TMessage>> DeserializeResponses<TMessage>(
            List<JObject> responses) where TMessage : class, new()
        {
            var serializer = new DataContractSerializer(typeof(TMessage));

            foreach (var resp in responses)
            {
                var isFault = resp.Value<bool>("isFault");
                var faultMessage = resp.Value<string>("faultMessage");
                var payload = resp.Value<string>("payload");
                var userData = resp.Value<string>("userData");

                var typed = new BrokerResponse<TMessage>
                {
                    UserData = userData,
                    IsFault = isFault,
                    FaultMessage = faultMessage
                };

                if (!isFault && payload != null)
                {
                    try
                    {
                        var bytes = Convert.FromBase64String(payload);
                        using (var ms = new MemoryStream(bytes))
                        {
                            typed.Result = (TMessage)serializer.ReadObject(ms);
                        }
                    }
                    catch (Exception ex)
                    {
                        typed.IsFault = true;
                        typed.FaultMessage = "Deserialization failed: " + ex.Message;
                    }
                }

                yield return typed;
            }
        }

        public void Close()
        {
            Dispose();
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
