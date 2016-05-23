using System;

namespace Azure.IoTHub.Lite
{
    public class SessionClosedException : Exception
    {
        public SessionClosedException(string message) : base(message) { }
    }
}