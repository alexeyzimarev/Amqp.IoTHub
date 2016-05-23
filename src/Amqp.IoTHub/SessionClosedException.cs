using System;

namespace Amqp.IoTHub
{
    public class SessionClosedException : Exception
    {
        public SessionClosedException(string message) : base(message) { }
    }
}