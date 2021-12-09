using System;
using System.Collections.Generic;

namespace Orleans.Configuration
{
    public class SqsOptions
    {
        /// <summary>
        /// Format: "Service=[service];SecretKey=[secretKey];AccessKey=[accessKey]".
        /// [service] - AWS region (example: us-west-1) or http(s) url for local SQS implementation.
        /// SecretKey and AccessKey are used for local SQS implementation only. You should use AWS IAM policies and roles for AWS SQS.
        /// </summary>
        [Redact]
        public string ConnectionString { get; set; }

        /// <summary>
        /// SQS attributes. Provider will use these values in case of creating queues.
        /// If you would like to use FIFO SQS queues, you should add FifoQueue attribute (see Amazon.SQS.Util.SQSConstants.ATTRIBUTE_FIFO_QUEUE).
        /// Another important attribute for FIFO queues is Amazon.SQS.Util.SQSConstants.ATTRIBUTE_CONTENT_BASED_DEDUPLICATION.
        /// See <see cref="FifoMessageDeduplicationIdGenerator"/>.
        /// </summary>
        public Dictionary<string, string> QueueAttributes { get; set; } = new();

        /// <summary>
        /// SQS tags. Provider will use these values in case of creating queues.
        /// </summary>
        public Dictionary<string, string> QueueTags { get; set; } = new();

        /// <summary>
        /// MessageGroupId value for FIFO queues. It will be used for sending all messages to FIFO queues.
        /// Default value is "0", but, for example, you can set silo-specific (producer) unique constant.
        /// </summary>
        public string FifoMessageGroupId { get; set; } = "0";

#nullable enable
        /// <summary>
        /// Generator of MessageDeduplicationId for FIFO queues.
        /// Must be <c>null</c> if you use "Content-based deduplication" for FIFO queues.
        /// Otherwise, a set delegate must return unique value for each call.
        /// Default value is <seealso cref="MessageDeduplicationIdGenerator.Next"/>.
        /// Provider uses the setting when queues are FIFO and Content-based deduplication is not enabled. See <see cref="QueueAttributes"/>.
        /// </summary>
        public Func<string>? FifoMessageDeduplicationIdGenerator { get; set; } = MessageDeduplicationIdGenerator.Next;
#nullable restore
    }
}
