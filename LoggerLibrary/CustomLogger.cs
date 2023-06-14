using Nest;
using System.Text;
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using System.Diagnostics;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using System.Runtime.CompilerServices;

namespace LoggerLibrary
{
    public class LogMessage
    {
        public StringBuilder? Message { get; set; }
        public DateTime TimeStamp { get; set; }
    }

    public class CustomLogger : ILogger
    {
        private readonly LoggerConfiguration _configuration;
        private static ObservableConcurrentQueue<LogMessage> _logQueue = new();
        private static int count = 0;
        private readonly IElasticClient _elasticClient;
        private static string _categoryName = string.Empty;
        private string _callingMethod;
        public CustomLogger(LoggerConfiguration logger, string categoryName, [CallerMemberName] string callingMethod = "")
        {
            _callingMethod = callingMethod;
            _configuration = logger;
            _categoryName=categoryName;
            if (_logQueue == null)
            {
                _logQueue = new ObservableConcurrentQueue<LogMessage>();
            }
            _logQueue.QueueChanged += LogQueue_QueueChanged;
            _elasticClient = CreateElasticClient();
        }

        private async void LogQueue_QueueChanged(object? sender, QueueChangedEventArgs<LogMessage> e)
        {
            try
            {
                if (sender!=null && ShouldProcessLogs(sender))
                {

                    // Clone the log queue to avoid potential race conditions during processing
                    var logMessages = new ObservableConcurrentQueue<LogMessage>(_logQueue);

                    // Clear the original queue before pushing to ElasticDB
                    await _logQueue.ClearAsync();

                    await PushToElasticDB(logMessages);
                }

            }
            catch (Exception ex)
            {
                // Handle the exception (e.g., log the error, retry the operation, etc.)
                Console.WriteLine($"Failed to push log messages to Elasticsearch. Error: {ex}");
            }
        }


        private bool ShouldProcessLogs(object sender)
        {
            var logMessages = (ObservableConcurrentQueue<LogMessage>)sender;
            if (_configuration.Count != 0 || _configuration.Time != 0)
            {
                var firstMsg = logMessages.GetFirstItem().TimeStamp;
                var secondMsg = logMessages.GetLastItem().TimeStamp;
                return logMessages.Count == _configuration.Count || (secondMsg - firstMsg).Minutes == _configuration.Time;
            }
            return false;
        }

        private async Task PushToElasticDB(ObservableConcurrentQueue<LogMessage> logMessages)
        {
            // Send log messages to the Elastic database using an ElasticClient instance
            var bulkRequest = new BulkDescriptor();
            var bulkResponse = await _elasticClient.BulkAsync(b => b
                .Index(_configuration.UniqueID)
                .IndexMany(logMessages, (bd, document) => bd
                    .Document(document)
                    .Routing(_configuration.UniqueID)
                ));

            if (!bulkResponse.IsValid)
            {
                // Handle error if the bulk request failed
                Console.WriteLine($"Failed to push log messages to Elasticsearch for registration id {_configuration.UniqueID}. Error: {bulkResponse.DebugInformation}");
            }
        }


        private static IElasticClient CreateElasticClient()
        {
            var connectionString = "http://localhost:9200";
            var settings = new ConnectionSettings(new Uri(connectionString));
            var elasticClient = new ElasticClient(settings);
            return elasticClient;
        }

        private async Task ConfigureIndexLifecyclePolicy(string indexName)
        {
            var updateIndexSettingsResponse = await _elasticClient.Indices.UpdateSettingsAsync(indexName, s => s
                .IndexSettings(i => i
                    .Setting("index.lifecycle.name", "lifecycle_policy") // Set the name of your index lifecycle policy
                    .Setting("index.lifecycle.rollover_alias", indexName)
                    .Setting("index.lifecycle.parse_origination_date", true)
                )
            );

            if (!updateIndexSettingsResponse.IsValid)
            {
                // Handle error if configuring the index lifecycle policy failed
                Console.WriteLine($"Failed to configure the index lifecycle policy for index {indexName}. Error: {updateIndexSettingsResponse.DebugInformation}");
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var (function, file, line) = ExtractMyFunctionAndLine(exception);

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
            var date = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(logMessage))
            {

                logBuilder.Append("\t[");
                logBuilder.Append(++count);
                logBuilder.Append("]\t");
                logBuilder.Append("\t[TimeStamp");
                logBuilder.Append(date);
                logBuilder.Append('\t');
                logBuilder.Append("\t[Level:");
                logBuilder.Append(GetShortLogLevel(logLevel) + ":");
                logBuilder.Append("]\t");
                logBuilder.Append("\t[User ID: ");
                logBuilder.Append(string.IsNullOrEmpty("") ? "Not established" : "");
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

            if (_configuration.LoggingMethod.HasValue && _configuration.LoggingMethod != 1)
            {
                //_ = LogMessageToQueue(sessionId, logBuilder, _configuration);
                //_ = LogMessageToQueue(logBuilder, _configuration);
                _logQueue.Enqueue(new LogMessage { Message = logBuilder, TimeStamp = date });
            }
            else
            {
                var logFilePath = GetLogFilePath();
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
                    if (_configuration.Time.HasValue && _configuration.Time > 0)
                    {
                        var fileInfo = new FileInfo(logFilePath);
                        shouldRollOver = fileInfo.LastWriteTime.AddDays(_configuration.Time.Value) < DateTime.UtcNow;
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

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        static string GetShortLogLevel(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "Trace",
                LogLevel.Debug => "Debug",
                LogLevel.Information => "Information",
                LogLevel.Warning => "Warning",
                LogLevel.Error => "Error",
                LogLevel.Critical => "Critical",
                _ => logLevel.ToString().ToUpper(),
            };
        }

        private string GetLogFilePath()
        {
            // Set the desired log file path based on the session ID or any other identifier
            return $"{_configuration.FilePath}/logs/{_configuration.UniqueID}.log";
        }

        private (string function, string file, string line) ExtractMyFunctionAndLine(Exception error)
        {
            if (error == null || error.StackTrace == null)
            {
                return (string.Empty, string.Empty, string.Empty);
            }

            var atText = "at ";
            var lineText = ":line ";
            var inText = " in ";
            var omittedStackLines = new[]
            {
                "at System.",
                "at Microsoft."
            };

            var stackLines = error.StackTrace
                .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim());
            var appStackLines = stackLines
                .Where(l => l.StartsWith(atText) && omittedStackLines.All(omitted => !l.StartsWith(omitted)));
            var myFunctionWithLineNumber = appStackLines
                .Where(l => l.Contains(lineText))
                .FirstOrDefault();
            var bestFunction = myFunctionWithLineNumber ?? // Use the top-most stack line that has an app function in it and a line number in it.
                appStackLines.FirstOrDefault() ?? // No line number? use the top-most stack line that has an app function in it.
                stackLines.LastOrDefault(l => l.Contains(lineText)) ?? // No app function? Use the top-most stack line that has a line number in it.
                stackLines.LastOrDefault(); // No line number? Use the top-most function.
            if (bestFunction != null)
            {
                // OK, we have a stack line that ideally looks something like:
                // "at MyCompany.FooBar.Blah() in c:\builds\foobar.cs:line 625"
                var indexOfIn = bestFunction.IndexOf(inText, atText.Length);
                if (indexOfIn == -1)
                {
                    // There's no space after the function. The stack line is likely "at MyCompany.Foobar.Blah()".
                    // This means there's no file or line number. Use only the function name.
                    var function = FunctionWithoutNamespaces(bestFunction.Substring(atText.Length));
                    return (function, string.Empty, string.Empty);
                }
                else
                {
                    // We have " in " after the function name, so we have a file.
                    var function = FunctionWithoutNamespaces(bestFunction[atText.Length..indexOfIn]);
                    var lineIndex = bestFunction.IndexOf(lineText);
                    var fileIndex = indexOfIn + inText.Length;
                    if (lineIndex == -1)
                    {
                        // We don't have a "line: 625" bit of text.
                        // We instead have "at MyCompany.FooBar.Blah() in c:\builds\foobar.cs"
                        var file = FileWithoutPath(bestFunction.Substring(fileIndex));
                        return (function, file, string.Empty);
                    }
                    else
                    {
                        // We have a line too. So, we have the ideal "at MyCompany.FooBar.Blah() in c:\builds\foobar.cs:line 625".
                        var file = FileWithoutPath(bestFunction[fileIndex..lineIndex]);
                        var line = bestFunction.Substring(lineIndex + lineText.Length);
                        return (function, file, line);
                    }
                }
            }

            return (string.Empty, string.Empty, string.Empty);
        }

        // This function takes a fully qualified function and turns it into just the class and function name:
        // MyApp.Sample.HomeController.Foo() -> HomeController.Foo()
        private static string FunctionWithoutNamespaces(string function)
        {
            if (string.IsNullOrEmpty(function))
            {
                return string.Empty;
            }

            var period = ".";
            var parts = function.Split(new[] { period }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 2)
            {
                return string.Join(period, parts.Skip(parts.Length - 2));
            }

            return function;
        }

        // Takes a file path and returns just the file name.
        // c:\foo\bar.cs -> bar.cs
        private static string FileWithoutPath(string file)
        {
            if (string.IsNullOrEmpty(file))
            {
                return string.Empty;
            }

            return System.IO.Path.GetFileName(file);
        }

    }

    public class CallingMethod
    {
        public string MethodName { get; set; }
        public string FilePath { get; set; }
    }
}

#region Commented Code
//public static async Task LogMessageToQueue(StringBuilder logMessage, LoggerConfiguration loggerConfiguration)
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
//        var msgbody = $"{DateTime.UtcNow}{separator}{loggerConfiguration.UniqueID}{separator}{logMessage}{separator}{loggerConfiguration.Count}{separator}{loggerConfiguration.Time}";
//        var sendMessageRequest = new SendMessageRequest
//        {
//            QueueUrl = "https://sqs.eu-west-2.amazonaws.com/942901483403/LoggerQueue.fifo",
//            MessageBody = msgbody,
//            MessageGroupId = loggerConfiguration.UniqueID
//        };

//        await client.SendMessageAsync(sendMessageRequest);
//    }
//    catch (Exception)
//    {

//    }
//}

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

#endregion