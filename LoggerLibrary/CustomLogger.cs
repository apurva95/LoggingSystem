using Nest;
using System.Text;
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using System.Diagnostics;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using System.Text.RegularExpressions;

namespace LoggerLibrary
{
    public class LogMessage
    {
        public string? Level { get; set; }
        public string? Message { get; set; }
        public DateTime TimeStamp { get; set; }
    }

    public class CustomLogger : ILogger
    {
        private readonly LoggerConfiguration _configuration;
        private static ObservableConcurrentQueue<LogMessage> _logQueue = new();
        private static int count = 0;
        private readonly IElasticClient _elasticClient;
        private static string _categoryName = string.Empty;
        private readonly Stopwatch _stopwatch = new();
        private static readonly SemaphoreSlim _semaphore = new(1);
        private static readonly object _fileLock = new();
        public CustomLogger(LoggerConfiguration logger, string categoryName)
        {
            _configuration = logger;
            _categoryName = categoryName;
            _logQueue ??= new ObservableConcurrentQueue<LogMessage>();
            _logQueue.QueueChanged += LogQueue_QueueChanged;
            _elasticClient = CreateElasticClient();
        }

        private async void LogQueue_QueueChanged(object sender, QueueChangedEventArgs<LogMessage> e)
        {
            try
            {
                if (ShouldProcessLogs(sender))
                {
                    await _semaphore.WaitAsync();
                    try
                    {
                        await ProcessLogQueueAsync();
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle the exception (e.g., log the error, retry the operation, etc.)
                Console.WriteLine($"Failed to push log messages to Elasticsearch. Error: {ex}");
            }
        }


        private async Task ProcessLogQueueAsync()
        {
            try
            {
                ObservableConcurrentQueue<LogMessage> logMessages;

                lock (_logQueue)
                {
                    logMessages = new ObservableConcurrentQueue<LogMessage>(_logQueue);
                    _logQueue.ClearAsync();
                }

                await PushToElasticDB(logMessages);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to push log messages to Elasticsearch. Error: {ex}");
            }
        }


        private bool ShouldProcessLogs(object sender)
        {
            var logMessages = (ObservableConcurrentQueue<LogMessage>)sender;
            if (_configuration.Count != 0 || _configuration.Time != 0)
            {
                return logMessages.Count == _configuration.Count || _stopwatch.Elapsed.TotalMinutes >= _configuration.Time;
            }
            return false;
        }

        private async Task PushToElasticDB(ObservableConcurrentQueue<LogMessage> logMessages)
        {
            if (logMessages.Count > 0)
            {
                Task<BulkResponse> bulkTask = null;
                try
                {
                    lock (_elasticClient)
                    {
                        try
                        {
                            var healthResponse = _elasticClient.Cluster.Health();
                            Console.WriteLine($"Cluster health: {healthResponse.Status}");
                            // Send log messages to the Elastic database using an ElasticClient instance
                            var bulkRequest = new BulkDescriptor();
                            bulkTask = _elasticClient.BulkAsync(b => b
                                .Index(_configuration.RegistrationID)
                                .IndexMany(logMessages, (bd, document) => bd
                                    .Document(document)
                                    .Routing(_configuration.RegistrationID)
                                ));
                        }
                        catch(Exception r)
                        {

                        }
                    }


                    var bulkResponse = await bulkTask;


                    if (bulkResponse == null || !bulkResponse.IsValid)
                    {
                        // Handle error if the bulk request failed
                        Console.WriteLine($"Failed to push log messages to Elasticsearch for registration id {_configuration.RegistrationID}. Error: {bulkResponse.DebugInformation}");
                    }
                }
                catch (Exception e)
                {

                }
            }
        }

        private static IElasticClient CreateElasticClient()
        {
            var connectionString = "http://localhost:9200/";
            var settings = new ConnectionSettings(new Uri(connectionString));
            var elasticClient = new ElasticClient(settings);
            return elasticClient;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            if (formatter == null)
            {
                throw new ArgumentNullException(nameof(formatter));
            }

            var logMessage = BuildLogMessage(logLevel, state, exception, formatter);

            if (_configuration.LoggingMethod.HasValue && _configuration.LoggingMethod != 1)
            {
                EnqueueLogMessage(logLevel, logMessage);
            }
            else
            {
                var logFilePath = GetLogFilePath();
                lock (_fileLock)
                {
                    WriteLogToFile(logFilePath, logMessage);
                    if (ShouldPerformLogRollover(logFilePath))
                    {
                        var newLogFilePath = GenerateNewLogFilePath(logFilePath);
                        MoveLogFile(logFilePath, newLogFilePath);
                        WriteLogToFile(logFilePath, string.Empty);
                    }
                }
            }
        }

        private static string BuildLogMessage<TState>(LogLevel logLevel, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var (function, _, line) = ExtractMyFunctionAndLine(exception);
            var logBuilder = new StringBuilder();
            var date = DateTime.UtcNow;

            var countValue = (++count).ToString(); // Assuming `count` is the variable storing the count value

            if (!string.IsNullOrEmpty(function) && !string.IsNullOrEmpty(line))
            {
                logBuilder.AppendLine($"[TimeStamp: {date}] [Level: {GetShortLogLevel(logLevel)}] [Calling File: {_categoryName}]");
            }
            else
            {
                logBuilder.AppendLine($"[TimeStamp: {date}] [Level: {GetShortLogLevel(logLevel)}] [Calling File: {_categoryName}]");
            }
            if (state != null)
            {
                var logMessage = formatter(state, exception);
                logBuilder.AppendLine($" [Message: {logMessage}]");
            }

            if (exception != null)
            {
                logBuilder.AppendLine(exception.ToString());
            }

            return logBuilder.ToString();
        }

        private static void EnqueueLogMessage(LogLevel logLevel, string logMessage)
        {
            _logQueue.Enqueue(new LogMessage { Level = GetShortLogLevel(logLevel), Message = logMessage, TimeStamp = DateTime.Now });
        }

        private static void WriteLogToFile(string logFilePath, string logMessage)
        {
            File.AppendAllText(logFilePath, logMessage);
        }

        private bool ShouldPerformLogRollover(string logFilePath)
        {
            var fileInfo = new FileInfo(logFilePath);

            if (_configuration.Time.HasValue && _configuration.Time > 0)
            {
                var lastWriteTimeUtc = fileInfo.LastWriteTime;
                var rolloverTime = lastWriteTimeUtc.AddMinutes(_configuration.Time.Value);
                if (rolloverTime < DateTime.Now)
                {
                    return true;
                }
            }

            if (_configuration.Count > 0)
            {
                var logContent = File.ReadAllText(logFilePath);
                var logEntries = logContent.Split(new[] { Environment.NewLine + "[" }, StringSplitOptions.RemoveEmptyEntries);
                if (logEntries.Length > 0)
                {
                    var lastLogEntry = logEntries.LastOrDefault();
                    if (!string.IsNullOrEmpty(lastLogEntry))
                    {
                        var match = Regex.Match(lastLogEntry, @"^(\d+)\]");
                        if (match.Success && match.Groups.Count > 1)
                        {
                            var numberValue = match.Groups[1].Value;
                            if (int.TryParse(numberValue, out int number))
                            {
                                return count >= _configuration.Count;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static void MoveLogFile(string sourceFilePath, string destinationFilePath)
        {
            File.Move(sourceFilePath, destinationFilePath);
        }

        private static string GenerateNewLogFilePath(string logFilePath)
        {
            var logDirectory = Path.GetDirectoryName(logFilePath);
            var logFileName = Path.GetFileNameWithoutExtension(logFilePath);
            var newLogFileName = $"{logFileName}_{DateTime.Now:yyyyMMddHHmmss}.log";
            return Path.Combine(logDirectory, newLogFileName);
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
            return $"{_configuration.FilePath}/logs/{_configuration.RegistrationID}.log";
        }

        private static (string function, string file, string line) ExtractMyFunctionAndLine(Exception error)
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
                    var function = FunctionWithoutNamespaces(bestFunction[atText.Length..]);
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
                        var file = FileWithoutPath(bestFunction[fileIndex..]);
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