using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using System.Text;

namespace LoggerLibrary
{
    public class CustomLogger : ILogger
    {
        private readonly LoggerConfiguration _configuration;
        private readonly ISessionIdProvider _sessionIdProvider;
        public CustomLogger(LoggerConfiguration logger, ISessionIdProvider sessionIdProvider) 
        {
            _configuration = logger;
            _sessionIdProvider = sessionIdProvider;

            // Get the Kafka configuration values
            //_producerConfig = new ProducerConfig { BootstrapServers = "localhost:9092" };
        }
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            //TODO: create model, get session id, get information->Queue
            if (!IsEnabled(logLevel))
            {
                return;
            }

            if (formatter == null)
            {
                throw new ArgumentNullException(nameof(formatter));
            }
            var logMessage = formatter(state, exception);

            var logBuilder = new StringBuilder();
            var sessionId = _sessionIdProvider.GetSessionId();
            if (!string.IsNullOrEmpty(logMessage))
            {
                logBuilder.Append(DateTimeOffset.Now.ToString("o"));
                logBuilder.Append('\t');
                logBuilder.Append(GetShortLogLevel(logLevel));
                logBuilder.Append("\t[");
                logBuilder.Append(eventId);
                logBuilder.Append("]\t");
                logBuilder.Append("\t[Session ID: ");
                logBuilder.Append(string.IsNullOrEmpty(sessionId)?"Not established":sessionId);
                logBuilder.Append("]\t");
                logBuilder.Append("\t[Message: ");
                logBuilder.Append(logMessage);
                logBuilder.Append("]\t");
            }

            if (exception != null)
            {
                // exception message
                logBuilder.AppendLine(exception.ToString());
            }

            if (!string.IsNullOrEmpty(sessionId))
            {
                _ = LogMessageToQueue(sessionId, logBuilder, _configuration);
            }

            //string folder = @"C:\Temp\";
            //string fileName = "CSharpCornerAuthors.txt";
            //string fullPath = folder + fileName;
            //using StreamWriter sw = File.AppendText(fullPath);
            //sw.WriteLine(logBuilder);
        }
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public static async Task LogMessageToQueue(string sessionId, StringBuilder logMessage, LoggerConfiguration loggerConfiguration)
        {
            try
            {
                var awsCredentials = new BasicAWSCredentials("AKIA5XCKO26FUZUKLTW7", "hP/NgUR2H9nIqn3ilanMHCbsdDV7iDTChGk7fNYv");
                var sqsConfig = new AmazonSQSConfig
                {
                    RegionEndpoint = Amazon.RegionEndpoint.EUWest2 // Replace with your desired region
                };

                using var client = new AmazonSQSClient(awsCredentials, sqsConfig);
                var separator = "|";
                var msgbody = $"{DateTime.UtcNow}{separator}{sessionId}{separator}{logMessage}{separator}{loggerConfiguration.Count}{separator}{loggerConfiguration.Time}";
                var sendMessageRequest = new SendMessageRequest
                {
                    QueueUrl = "https://sqs.eu-west-2.amazonaws.com/942901483403/LoggerQueue.fifo",
                    MessageBody = msgbody,
                    MessageGroupId = sessionId
                };

                await client.SendMessageAsync(sendMessageRequest);
            }
            catch (Exception)
            {

            }
        }



        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        static string GetShortLogLevel(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "TRCE",
                LogLevel.Debug => "DBUG",
                LogLevel.Information => "INFO",
                LogLevel.Warning => "WARN",
                LogLevel.Error => "FAIL",
                LogLevel.Critical => "CRIT",
                _ => logLevel.ToString().ToUpper(),
            };
        }
    }
}