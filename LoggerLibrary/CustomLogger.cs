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
        private static int count = 0;
        public CustomLogger(LoggerConfiguration logger, ISessionIdProvider sessionIdProvider) 
        {
            _configuration = logger;
            _sessionIdProvider = sessionIdProvider;
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
                logBuilder.Append("\t[");
                logBuilder.Append(++count);
                logBuilder.Append("]\t");
                logBuilder.Append(DateTimeOffset.UtcNow.ToString("dd-MM-yyyy hh:mm:ss"));
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

            if (_configuration.LoggingMethod != 1 && !string.IsNullOrEmpty(sessionId))
            {
                //_ = LogMessageToQueue(sessionId, logBuilder, _configuration);
                _ = LogMessageToQueue(logBuilder, _configuration);
            }
            else
            {
                var logFilePath = GetLogFilePath(sessionId);
                // Ensure the directory exists
                var logDirectory = Path.GetDirectoryName(logFilePath);
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
                // Append the message to the log file
                File.AppendAllText(logFilePath, logBuilder.ToString());
                // Implement rollover based on the Time and Count properties if required
                if (_configuration.Time > 0 || _configuration.Count > 0)
                {
                    // Check if the log file needs to roll over based on the configured time or count limits
                    bool shouldRollOver = false;
                    if (_configuration.Time > 0)
                    {
                        var fileInfo = new FileInfo(logFilePath);
                        shouldRollOver = fileInfo.LastWriteTime.AddDays(_configuration.Time) < DateTime.UtcNow;
                    }
                    if (!shouldRollOver && _configuration.Count > 0)
                    {
                        shouldRollOver = Directory.GetFiles(Path.GetDirectoryName(logFilePath)).Length >= _configuration.Count;
                    }

                    // Implement the rollover logic (e.g., create a new file, clear the existing file, etc.) based on your requirements
                    if (shouldRollOver)
                    {
                        // Example: Create a new log file with a timestamp in the name
                        var newLogFilePath = Path.Combine(Path.GetDirectoryName(logFilePath), $"{Path.GetFileNameWithoutExtension(logFilePath)}_{DateTime.Now:yyyyMMddHHmmss}.log");
                        File.Create(newLogFilePath).Dispose();
                        // Example: Clear the existing log file
                        // File.WriteAllText(logFilePath, string.Empty);
                    }
                }
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

        public static async Task LogMessageToQueue(StringBuilder logMessage, LoggerConfiguration loggerConfiguration)
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
                var msgbody = $"{DateTime.UtcNow}{separator}{loggerConfiguration.UniqueID}{separator}{logMessage}{separator}{loggerConfiguration.Count}{separator}{loggerConfiguration.Time}";
                var sendMessageRequest = new SendMessageRequest
                {
                    QueueUrl = "https://sqs.eu-west-2.amazonaws.com/942901483403/LoggerQueue.fifo",
                    MessageBody = msgbody,
                    MessageGroupId = loggerConfiguration.UniqueID
                };

                await client.SendMessageAsync(sendMessageRequest);
            }
            catch (Exception)
            {

            }
        }

        //public static async Task LogMessageToQueue(string sessionId, StringBuilder logMessage, LoggerConfiguration loggerConfiguration)
        //{
        //    try
        //    {
        //        var awsCredentials = new BasicAWSCredentials("AKIA5XCKO26FUZUKLTW7", "hP/NgUR2H9nIqn3ilanMHCbsdDV7iDTChGk7fNYv");
        //        var sqsConfig = new AmazonSQSConfig
        //        {
        //            RegionEndpoint = Amazon.RegionEndpoint.EUWest2 // Replace with your desired region
        //        };

        //        using var client = new AmazonSQSClient(awsCredentials, sqsConfig);
        //        var separator = "|";
        //        var msgbody = $"{DateTime.UtcNow}{separator}{sessionId}{separator}{logMessage}{separator}{loggerConfiguration.Count}{separator}{loggerConfiguration.Time}";
        //        var sendMessageRequest = new SendMessageRequest
        //        {
        //            QueueUrl = "https://sqs.eu-west-2.amazonaws.com/942901483403/LoggerQueue.fifo",
        //            MessageBody = msgbody,
        //            MessageGroupId = sessionId
        //        };

        //        await client.SendMessageAsync(sendMessageRequest);
        //    }
        //    catch (Exception)
        //    {

        //    }
        //}



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

        private string GetLogFilePath(string sessionId)
        {
            // Set the desired log file path based on the session ID or any other identifier
            return $"{_configuration.FilePath}/logs/{sessionId}.log";
        }
    }
}