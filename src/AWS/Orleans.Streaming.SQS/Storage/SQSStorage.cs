using Amazon.Runtime;
using Amazon.SQS;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.SQS.Model;
using Amazon.SQS.Util;
using Microsoft.Extensions.Logging;
using Orleans.Streaming.SQS;
using SQSMessage = Amazon.SQS.Model.Message;
using Orleans;
using Orleans.Configuration;

namespace OrleansAWSUtils.Storage
{
    /// <summary>
    /// Wrapper/Helper class around AWS SQS queue service
    /// </summary>
    internal class SQSStorage
    {
        /// <summary>
        /// Maximum number of messages allowed by SQS to peak per request
        /// </summary>
        public const int MAX_NUMBER_OF_MESSAGE_TO_PEAK = 10;
        private const string AccessKeyPropertyName = "AccessKey";
        private const string SecretKeyPropertyName = "SecretKey";
        private const string ServicePropertyName = "Service";
        private ILogger Logger;
#nullable enable
        private readonly IDictionary<string, string>? _attributes;
        private readonly IDictionary<string, string>? _tags;
#nullable restore
        private string accessKey;
        private string secretKey;
        private string service;
        private string queueUrl;
        private AmazonSQSClient sqsClient;

        /// <summary>
        /// The queue Name
        /// </summary>
        public string QueueName { get; private set; }

        /// <summary>
        /// The queue is FIFO and <see cref="QueueName"/> has ".fifo" suffix
        /// </summary>
        public bool IsFifo { get; }

        public bool UsesContentBasedDeduplication { get; }

        /// <summary>
        /// Default Ctor
        /// </summary>
        /// <param name="loggerFactory">logger factory to use</param>
        /// <param name="queueName">The name of the queue</param>
        /// <param name="serviceId">The service ID</param>
        /// <param name="sqsOptions">SQS options</param>
        public SQSStorage(ILoggerFactory loggerFactory, string queueName, string serviceId, SqsOptions sqsOptions)
        {
            QueueName = string.IsNullOrWhiteSpace(serviceId) ? queueName : $"{serviceId}-{queueName}";
            ParseDataConnectionString(sqsOptions.ConnectionString);
            _attributes = sqsOptions.QueueAttributes;
            _tags = sqsOptions.QueueTags;
            IsFifo = _attributes?.ContainsKey(SQSConstants.ATTRIBUTE_FIFO_QUEUE) == true;
            if (IsFifo)
            {
                QueueName += ".fifo";
            }
            UsesContentBasedDeduplication = _attributes?.ContainsKey(SQSConstants.ATTRIBUTE_CONTENT_BASED_DEDUPLICATION) == true;
            Logger = loggerFactory.CreateLogger<SQSStorage>();
            CreateClient();
        }

        private void ParseDataConnectionString(string dataConnectionString)
        {
            var parameters = dataConnectionString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            var serviceConfig = parameters.Where(p => p.Contains(ServicePropertyName)).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(serviceConfig))
            {
                var value = serviceConfig.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
                if (value.Length == 2 && !string.IsNullOrWhiteSpace(value[1]))
                    service = value[1];
            }

            var secretKeyConfig = parameters.Where(p => p.Contains(SecretKeyPropertyName)).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(secretKeyConfig))
            {
                var value = secretKeyConfig.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
                if (value.Length == 2 && !string.IsNullOrWhiteSpace(value[1]))
                    secretKey = value[1];
            }

            var accessKeyConfig = parameters.Where(p => p.Contains(AccessKeyPropertyName)).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(accessKeyConfig))
            {
                var value = accessKeyConfig.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
                if (value.Length == 2 && !string.IsNullOrWhiteSpace(value[1]))
                    accessKey = value[1];
            }
        }

        private void CreateClient()
        {
            if (service.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                service.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Local SQS instance (for testing)
                var credentials = new BasicAWSCredentials("dummy", "dummyKey");
                sqsClient = new AmazonSQSClient(credentials, new AmazonSQSConfig { ServiceURL = service });
            }
            else if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
            {
                // AWS SQS instance (auth via explicit credentials)
                var credentials = new BasicAWSCredentials(accessKey, secretKey);
                sqsClient = new AmazonSQSClient(credentials, new AmazonSQSConfig { RegionEndpoint = AWSUtils.GetRegionEndpoint(service) });
            }
            else
            {
                // AWS SQS instance (implicit auth - EC2 IAM Roles etc)
                sqsClient = new AmazonSQSClient(new AmazonSQSConfig { RegionEndpoint = AWSUtils.GetRegionEndpoint(service) });
            }
        }

#nullable enable
        private async Task<string?> GetQueueUrl()
        {
            try
            {
                var response = await sqsClient.GetQueueUrlAsync(QueueName);
                if (!string.IsNullOrWhiteSpace(response.QueueUrl))
                    queueUrl = response.QueueUrl;

                return queueUrl;
            }
            catch (QueueDoesNotExistException)
            {
                return null;
            }
        }
#nullable restore

        /// <summary>
        /// Initialize SQSStorage by creating or connecting to an existent queue
        /// </summary>
        /// <returns></returns>
        public async Task InitQueueAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(await GetQueueUrl()))
                {
                    var request = new CreateQueueRequest(this.QueueName);
                    if (_attributes?.Count > 0)
                    {
                        foreach (var attribute in _attributes)
                        {
                            request.Attributes.Add(attribute.Key, attribute.Value);
                        }
                    }
                    if (_tags?.Count > 0)
                    {
                        foreach (var tag in _tags)
                        {
                            request.Tags.Add(tag.Key, tag.Value);
                        }
                    }
                    var response = await this.sqsClient.CreateQueueAsync(request);
                    queueUrl = response.QueueUrl;
                }
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "InitQueueAsync", ErrorCode.StreamProviderManagerBase);
            }
        }

        /// <summary>
        /// Delete the queue
        /// </summary>
        /// <returns></returns>
        public async Task DeleteQueue()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(queueUrl))
                    throw new InvalidOperationException("Queue not initialized");
                await sqsClient.DeleteQueueAsync(queueUrl);
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "DeleteQueue", ErrorCode.StreamProviderManagerBase);
            }
        }

        /// <summary>
        /// Add a message to the SQS queue
        /// </summary>
        /// <param name="message">Message request</param>
        /// <returns></returns>
        public async Task AddMessage(SendMessageRequest message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(queueUrl))
                    throw new InvalidOperationException("Queue not initialized");

                message.QueueUrl = queueUrl;
                await sqsClient.SendMessageAsync(message);
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "AddMessage", ErrorCode.StreamProviderManagerBase);
            }
        }

        /// <summary>
        /// Get Messages from SQS Queue.
        /// </summary>
        /// <param name="count">The number of messages to peak. Min 1 and max 10</param>
        /// <returns>Collection with messages from the queue</returns>
        public async Task<IEnumerable<SQSMessage>> GetMessages(int count = 1)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(queueUrl))
                    throw new InvalidOperationException("Queue not initialized");

                if (count < 1)
                    throw new ArgumentOutOfRangeException(nameof(count));

                var request = new ReceiveMessageRequest { QueueUrl = queueUrl, MaxNumberOfMessages = count <= MAX_NUMBER_OF_MESSAGE_TO_PEAK ? count : MAX_NUMBER_OF_MESSAGE_TO_PEAK };
                var response = await sqsClient.ReceiveMessageAsync(request);
                return response.Messages;
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "GetMessages", ErrorCode.StreamProviderManagerBase);
            }
            return null;
        }

        /// <summary>
        /// Delete messages from SQS queue
        /// </summary>
        /// <param name="messages">The messages to be deleted</param>
        /// <returns></returns>
        public async Task DeleteMessagesAsync(ICollection<SQSMessage> messages)
        {
            try
            {
                if (messages == null)
                    throw new ArgumentNullException(nameof(messages));

                if (messages.Count == 0)
                    return;

                if (messages.Any(message => string.IsNullOrWhiteSpace(message.ReceiptHandle)))
                    throw new ArgumentException($"{nameof(SQSMessage.ReceiptHandle)} should be neither null nor white space for each item", nameof(messages));

                if (string.IsNullOrWhiteSpace(queueUrl))
                    throw new InvalidOperationException("Queue not initialized");

                var entries = messages
                    .Select((message, i) => new DeleteMessageBatchRequestEntry(i.ToString(), message.ReceiptHandle))
                    .ToList();

                await sqsClient.DeleteMessageBatchAsync(new DeleteMessageBatchRequest(this.queueUrl, entries));
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "GetMessages", ErrorCode.StreamProviderManagerBase);
            }
        }

        private void ReportErrorAndRethrow(Exception exc, string operation, ErrorCode errorCode)
        {
            var errMsg = String.Format(
                "Error doing {0} for SQS queue {1} " + Environment.NewLine
                + "Exception = {2}", operation, QueueName, exc);
            Logger.Error(errorCode, errMsg, exc);
            throw new AggregateException(errMsg, exc);
        }
    }
}
