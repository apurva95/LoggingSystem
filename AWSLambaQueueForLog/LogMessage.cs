using Amazon.Lambda.Core;
using Elasticsearch.Net;
using LoggerLibrary;

namespace AWSLambaQueueForLog
{

    public class LogMessage
    {
        public string SessionId { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public LoggerConfiguration LoggerConfiguration { get; set; }
    }
}