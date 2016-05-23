using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Amqp;
using Amqp.Framing;
using Amqp.Types;
using Azure.IoTHub.Lite.Logging;

namespace Azure.IoTHub.Lite
{
    public class IoTHubDevice : IDisposable, IObservable<Message>
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private readonly Connection _connection;
        private readonly string _url;
        private readonly string _deviceKey;
        public readonly string Id;
        private Session _session;
        private SenderLink _senderLink;

        private readonly int _ttl = 2;

        private readonly List<IObserver<Message>> _observers;

        protected IoTHubDevice(Connection connection, string url, string deviceId, string deviceKey)
        {
            _connection = connection;
            _url = url;
            _deviceKey = deviceKey;
            Id = deviceId;

            _connection.Closed += (sender, error) => Log.WarnFormat("Connection closed with error {error}", error);

            _observers = new List<IObserver<Message>>();
        }

        public static async Task<IoTHubDevice> Connect(Connection connection, string url, string deviceId, string deviceKey)
        {
            var device = new IoTHubDevice(connection, url, deviceId, deviceKey);
            var connected = await device.Authenticate();
            if (connected) device.Open();
            return connected ? device : null;
        }

        public async Task SendMessage(string messageType, IDictionary<string, object> data, string body)
        {
            var message = new Message
            {
                Properties = new Properties {Subject = messageType},
                ApplicationProperties = new ApplicationProperties(),
                BodySection = new Data { Binary = Encoding.UTF8.GetBytes(body) }
            };
            foreach (var o in data)
                message.ApplicationProperties[o.Key] = o.Value;
            await _senderLink.SendAsync(message);
        }

        private void Open()
        {
            _session = new Session(_connection);
            _session.Closed += (sender, error) =>
            {
                var exception = new SessionClosedException(error.Description);
                foreach (var observer in _observers)
                    observer.OnError(exception);
            };

            var entity = $"/devices/{Id}/messages/events";
            _senderLink = new SenderLink(_session, "sender-link", entity);

            entity = $"/devices/{Id}/messages/deviceBound";
            var receiver = new ReceiverLink(_session, "receiver-link", entity);
            receiver.Start(5, OnMessage);
        }

        private void OnMessage(ReceiverLink receiver, Message message)
        {
            Log.DebugFormat("Message received {body}", Convert.ToBase64String((byte[]) message.Body));

            foreach (var observer in _observers)
                observer.OnNext(message);

            receiver.Accept(message);
            receiver.SetCredit(5);
        }

        private async Task<bool> Authenticate()
        {
            Observable.Interval(TimeSpan.FromMinutes(_ttl))
                .Subscribe(async _ => await PutCbsToken());
            return await PutCbsToken();
        }

        private async Task<bool> PutCbsToken()
        {
            var token = GetSasToken(_deviceKey, _url, TimeSpan.FromMinutes(_ttl));
            return await PutCbsToken(_connection, token, _url);
        }

        private static async Task<bool> PutCbsToken(Connection connection, string shareAccessSignature, string audience)
        {
            Log.Debug("Sending authentication token");
            bool result = true;
            var session = new Session(connection);

            const string cbsReplyToAddress = "cbs-reply-to";
            var cbsSender = new SenderLink(session, "cbs-sender", "$cbs");
            var cbsReceiver = new ReceiverLink(session, cbsReplyToAddress, "$cbs");

            // construct the put-token message
            var request = new Message(shareAccessSignature)
            {
                Properties = new Properties
                {
                    MessageId = Guid.NewGuid().ToString(),
                    ReplyTo = cbsReplyToAddress
                },
                ApplicationProperties = new ApplicationProperties
                {
                    ["operation"] = "put-token",
                    ["type"] = "azure-devices.net:sastoken",
                    ["name"] = audience
                }
            };
            await cbsSender.SendAsync(request);

            // receive the response
            var response = await cbsReceiver.ReceiveAsync();
            if (response?.Properties == null || response.ApplicationProperties == null)
            {
                result = false;
            }
            else
            {
                var statusCode = (int)response.ApplicationProperties["status-code"];
                var statusCodeDescription = (string)response.ApplicationProperties["status-description"];
                if (statusCode != 202 && statusCode != 200)
                {
                    result = false;
                    Log.ErrorFormat("Authentication failure {status}", statusCodeDescription);
                }
            }

            // the sender/receiver may be kept open for refreshing tokens
            await cbsSender.CloseAsync();
            await cbsReceiver.CloseAsync();
            await session.CloseAsync();
            Log.Debug("Authentication complete");

            return result;
        }

        private static string GetSasToken(string keyValue, string requestUri, TimeSpan ttl)
        {
            var expiry = ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) + ttl).TotalSeconds).ToString();
            var encodedUri = HttpUtility.UrlEncode(requestUri);

            byte[] hmac = SHA.computeHMAC_SHA256(Convert.FromBase64String(keyValue), Encoding.UTF8.GetBytes(encodedUri + "\n" + expiry));
            string sig = Convert.ToBase64String(hmac);
            return $"SharedAccessSignature sig={HttpUtility.UrlEncode(sig)}&se={HttpUtility.UrlEncode(expiry)}&sr={encodedUri}";
        }

        public void Dispose()
        {
            _connection?.Close(6000);
        }

        public IDisposable Subscribe(IObserver<Message> observer)
        {
            if (!_observers.Contains(observer))
                _observers.Add(observer);

            return Disposable.Create(() => _observers.Remove(observer));
        }

    }
}