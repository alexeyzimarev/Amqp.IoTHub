using System;
using System.Threading.Tasks;
using Amqp;
using Azure.IoTHub.Lite.Logging;

namespace Azure.IoTHub.Lite
{
    public class IoTHubConnection
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private readonly int _ttl;
        private readonly string _resourceUri;
        private readonly Address _address;
        private readonly ConnectionFactory _connectionFactory;

        public IoTHubConnection(string host, int port, int ttl = 60, bool varboseTrace = false)
        {
            _ttl = ttl;
            Connection.DisableServerCertValidation = true;

            _resourceUri = $"{host}/devices/";
            if (varboseTrace)
            {
                Trace.TraceLevel = TraceLevel.Verbose | TraceLevel.Frame;
                Trace.TraceListener = (f, a) => Log.Debug(DateTime.Now.ToString("[hh:ss.fff]") + " " +
                                                          string.Format(f, a));
            }
            _address = new Address(host, port);
            _connectionFactory = new ConnectionFactory();
        }

        public async Task<IoTHubDevice> ConnectDevice(string deviceId, string deviceKey)
        {
            var url = _resourceUri + deviceId;
            var connection = await _connectionFactory.CreateAsync(_address);
            var hub = await IoTHubDevice.Connect(connection, url, deviceId, deviceKey, _ttl);
            return hub;
        }

    }
}