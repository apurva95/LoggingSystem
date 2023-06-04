using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using LoggerLibrary;
using Nest;


namespace AWSLambaQueueForLog
{

    public class AWSLambdaTrigger
    {
        private readonly IAmazonSQS _sqsClient;
        private readonly IElasticClient _elasticClient;
        private readonly IDictionary<string, List<string>> _logQueue;

        public AWSLambdaTrigger()
        {
            _sqsClient = new AmazonSQSClient();
            _elasticClient = CreateElasticClient();
            _logQueue = new Dictionary<string, List<string>>();
        }

        public async Task ProcessLogs(SQSEvent sqsEvent, ILambdaContext context)
        {
            foreach (var sqsMessage in sqsEvent.Records)
            {
                var logMessage = sqsMessage.Body;
                var sessionConfig = GetSessionConfiguration(sqsMessage);

                // Extract session ID from the log message or SQS message attributes
                var sessionId = GetSessionId(logMessage, sqsMessage);

                // Add the log message to the queue for the corresponding session ID
                if (!_logQueue.ContainsKey(sessionId))
                {
                    _logQueue[sessionId] = new List<string>();
                }
                _logQueue[sessionId].Add(logMessage);

                // Determine if logs for the session should be processed and pushed to Elastic database
                if (ShouldProcessLogs(sessionId, sessionConfig))
                {
                    await ProcessAndPushLogs(sessionId);
                }
            }
        }

        private LoggerConfiguration GetSessionConfiguration(SQSEvent.SQSMessage sqsMessage)
        {
            // Extract session configuration from the message body or SQS message attributes
            // Parse the JSON or any other format used to store the configuration
            var sessionConfigJson = sqsMessage.MessageAttributes["LoggerConfiguration"].StringValue;
            var sessionConfig = DeserializeSessionConfiguration(sessionConfigJson);
            return sessionConfig;
        }

        private string GetSessionId(string logMessage, SQSEvent.SQSMessage sqsMessage)
        {
            // Extract the session ID from the log message or SQS message attributes
            // ...
            return sqsMessage.MessageAttributes["SessionID"].StringValue;
            //var sessionConfig = DeserializeSessionConfiguration(sessionConfigJson);
            //return sessionConfig; // Placeholder code for demonstration purposes
        }

        private bool ShouldProcessLogs(string sessionId, LoggerConfiguration sessionConfig)
        {
            // Determine if the logs for the session should be processed and pushed to Elastic database
            // Implement your custom logic based on count or time elapsed in the session configuration
            // ...

            return true; // Return true for demonstration purposes
        }

        private async Task ProcessAndPushLogs(string sessionId)
        {
            var logMessages = _logQueue[sessionId];

            // Process log messages for the session
            foreach (var logMessage in logMessages)
            {
                // Process log message
                // ...
            }

            // Send log messages to Elastic database in bulk
            await PushToElasticDB(sessionId, logMessages);

            // Clear the log messages queue for the session
            _logQueue[sessionId].Clear();
        }

        private async Task PushToElasticDB(string sessionId, List<string> logMessages)
        {
            // Send log messages to the Elastic database using an ElasticClient instance
            var bulkRequest = new BulkDescriptor();

            foreach (var logMessage in logMessages)
            {
                var bulkResponse = await _elasticClient.BulkAsync(b => b
                    .Index("logs")
                    .IndexMany(logMessages, (bd, document) => bd
                    .Document(document)
                    .Routing(sessionId)
                    ));

                if (!bulkResponse.IsValid)
                {
                    // Handle error if the bulk request failed
                    Console.WriteLine($"Failed to push log messages to Elasticsearch for session {sessionId}. Error: {bulkResponse.DebugInformation}");
                }
            }
        }

        private IElasticClient CreateElasticClient()
        {
            var connectionString = "http://your-elasticsearch-endpoint:9200";
            var settings = new ConnectionSettings(new Uri(connectionString));
            var elasticClient = new ElasticClient(settings);
            return elasticClient;
        }

        private LoggerConfiguration DeserializeSessionConfiguration(string sessionConfigJson)
        {
            // Deserialize the session configuration from JSON or any other format
            // ...
            return new LoggerConfiguration { Count = sessionConfigJson[0], Time = sessionConfigJson[1], LoggingMethod = 0 }; // Placeholder code for demonstration purposes
        }
    }
}
