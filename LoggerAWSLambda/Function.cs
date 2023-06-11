using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Nest;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Globalization;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace LoggerAWSLambda;

public class LogMessage
{
    public string? Message { get; set; }
    public DateTime TimeStamp { get; set; }
}

public class Function
{
    /// <summary>
    /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
    /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
    /// region the Lambda function is executed in.
    /// </summary>
    private readonly IAmazonSQS _sqsClient;
    private readonly IElasticClient _elasticClient;
    private static IDictionary<string, ConcurrentQueue<LogMessage>> _logQueue;
    private readonly IAmazonDynamoDB _dynamoDBClient;

    public Function()
    {
        _sqsClient = new AmazonSQSClient();
        _elasticClient = CreateElasticClient();
        _logQueue = new ConcurrentDictionary<string, ConcurrentQueue<LogMessage>>();
        _dynamoDBClient = new AmazonDynamoDBClient();
    }


    /// <summary>
    /// This method is called for every Lambda invocation. This method takes in an SQS event object and can be used 
    /// to respond to SQS messages.
    /// </summary>
    /// <param name="evnt"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        foreach (var sqsMessage in sqsEvent.Records)
        {
            var logMessage = sqsMessage.Body;
            var messageParts = logMessage.Split('|');
            // Extract the individual values from the message parts
            var currentTime = messageParts[0];
            var sessionId = messageParts[1];
            var message = messageParts[2];
            var count = Convert.ToInt32(messageParts[3]);
            var time = Convert.ToInt32(messageParts[4]);

            // Extract session ID from the log message or SQS message attributes

            // Add the log message to the queue for the corresponding session ID
            if (!_logQueue.ContainsKey(sessionId))
            {
                // Load the log queue from DynamoDB if it doesn't exist in memory
                var loadedQueue = await LoadLogQueueFromDynamoDB(sessionId);
                if (loadedQueue != null)
                {
                    _logQueue[sessionId] = loadedQueue;
                }
                else
                {
                    _logQueue[sessionId] = new ConcurrentQueue<LogMessage>();
                }
            }
            string format = "dd-MM-yyyy HH:mm:ss";
            DateTime dateTime = DateTime.ParseExact(currentTime, format, CultureInfo.InvariantCulture);
            _logQueue[sessionId].Enqueue(new LogMessage { Message = message, TimeStamp = dateTime });

            // Save the updated log queue to DynamoDB
            await SaveLogQueueToDynamoDB(sessionId);
            // Determine if logs for the session should be processed and pushed to Elastic database
            if (ShouldProcessLogs(sessionId, count, time))
            {
                await ProcessAndPushLogs(sessionId, sqsMessage);
            }
        }

    }

    private async Task<ConcurrentQueue<LogMessage>> LoadLogQueueFromDynamoDB(string sessionId)
    {
        var tableName = "LogQueueTable";
        var getItemRequest = new GetItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                { "SessionId", new AttributeValue { S = sessionId } }
            },
        };

        var getItemResponse = await _dynamoDBClient.GetItemAsync(getItemRequest);
        if (getItemResponse.Item.TryGetValue("LogQueue", out var logQueueAttributeValue) &&
            logQueueAttributeValue.S != null)
        {
            // Deserialize the log queue from the attribute value
            var logQueueJson = logQueueAttributeValue.S;
            return JsonConvert.DeserializeObject<ConcurrentQueue<LogMessage>>(logQueueJson);
        }

        return null;
    }

    private async Task SaveLogQueueToDynamoDB(string sessionId)
    {
        var tableName = "LogQueueTable";
        var logQueueJson = JsonConvert.SerializeObject(_logQueue[sessionId]);

        var updateItemRequest = new UpdateItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                { "SessionId", new AttributeValue { S = sessionId } }
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                { ":logQueue", new AttributeValue { S = logQueueJson } }
            },
            UpdateExpression = "SET LogQueue = :logQueue"
        };
        await _dynamoDBClient.UpdateItemAsync(updateItemRequest);
    }

    private async Task ProcessAndPushLogs(string sessionId, SQSEvent.SQSMessage sqsMessage)
    {
        var logMessages = _logQueue[sessionId];

        // Send log messages to Elastic database in bulk
        await PushToElasticDB(sessionId, logMessages);

        // Clear the log messages queue for the session
        _logQueue[sessionId].Clear();
        _logQueue.Remove(sessionId);

        var request = new DeleteItemRequest
        {
            TableName = "LogQueueTable",
            Key = new Dictionary<string, AttributeValue>
            {
                { "SessionId", new AttributeValue { S = sessionId } }
            }
        };

        await _dynamoDBClient.DeleteItemAsync(request);

        //// Delete the processed message from the SQS queue
        //var deleteRequest = new DeleteMessageRequest
        //{
        //    QueueUrl = sqsMessage.EventSourceArn,
        //    ReceiptHandle = sqsMessage.ReceiptHandle
        //};
        //await _sqsClient.DeleteMessageAsync(deleteRequest);
    }

    private static bool ShouldProcessLogs(string sessionId, int count, int time)
    {
        // Determine if the logs for the session should be processed and pushed to Elastic database
        // Implement your custom logic based on count or time elapsed in the session configuration
        // ...
        if (!_logQueue.ContainsKey(sessionId))
        {
            return false;
        }
        var logMessages = _logQueue[sessionId];
        if (count != 0)
        {
            return logMessages.Count == count;
        }
        if (time != 0)
        {
            var firstMsg = logMessages.First().TimeStamp;
            var secondMsg = logMessages.Last().TimeStamp;
            return (secondMsg - firstMsg).Minutes == time;
        }

        return false; // Return true for demonstration purposes
    }

    private async Task PushToElasticDB(string sessionId, ConcurrentQueue<LogMessage> logMessages)
    {
        // Send log messages to the Elastic database using an ElasticClient instance
        var bulkRequest = new BulkDescriptor();

        foreach (var logMessage in logMessages)
        {
            var bulkResponse = await _elasticClient.BulkAsync(b => b
                .Index(sessionId)
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
        // Configure the index lifecycle policy
        await ConfigureIndexLifecyclePolicy(sessionId);
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
}