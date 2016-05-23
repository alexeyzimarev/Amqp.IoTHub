using System;
using System.Collections.Generic;
using System.Configuration;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amqp;
using global::Serilog;

namespace Azure.IoTHub.Lite.Example
{
    class Program
    {
        private static void Main()
        {
            var log = new LoggerConfiguration()
                .WriteTo.ColoredConsole()
                .MinimumLevel.Debug()
                .CreateLogger();

            Log.Logger = log;

            var connection = new IoTHubConnection(ConfigurationManager.AppSettings["hub-host"], 5671);
            var device = connection.ConnectDevice(ConfigurationManager.AppSettings["device-id"],
                ConfigurationManager.AppSettings["device-key"]).Result;

            Observable.Timer(DateTimeOffset.Now, TimeSpan.FromMinutes(1))
                .Subscribe(async _ => await SendAliveMessage(device));

            device.Subscribe(OnNext, exception => Log.Error(exception, "Error occured"));

            log.Information("Sending alive events every minute and listening for commands...");

            Thread.Sleep(Timeout.Infinite);
        }

        private static async Task SendAliveMessage(IoTHubDevice device)
        {
            var data = new Dictionary<string, object> { {"device", device.Id} };
            await device.SendMessage("alive", data, "I am alive!");
        }

        private static void OnNext(Message message)
        {
            Log.Information("Message received {body}", Encoding.UTF8.GetString((byte[]) message.Body));
        }
    }
}
