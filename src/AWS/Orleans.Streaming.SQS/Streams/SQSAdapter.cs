using Orleans.Streams;
using OrleansAWSUtils.Storage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Serialization;

namespace OrleansAWSUtils.Streams
{
    internal class SQSAdapter : IQueueAdapter
    {
        protected readonly string ServiceId;
        private readonly SerializationManager serializationManager;
        private readonly SqsOptions _sqsOptions;
        private readonly IConsistentRingStreamQueueMapper streamQueueMapper;
        protected readonly ConcurrentDictionary<QueueId, SQSStorage> Queues = new ConcurrentDictionary<QueueId, SQSStorage>();
        private readonly ILoggerFactory loggerFactory;
        public string Name { get; private set; }
        public bool IsRewindable { get { return false; } }

        public StreamProviderDirection Direction { get { return StreamProviderDirection.ReadWrite; } }

        public SQSAdapter(SerializationManager serializationManager, IConsistentRingStreamQueueMapper streamQueueMapper,
            ILoggerFactory loggerFactory, SqsOptions sqsOptions, string serviceId, string providerName)
        {
            if (sqsOptions == null) throw new ArgumentNullException(nameof(sqsOptions));
            if (string.IsNullOrEmpty(sqsOptions.ConnectionString))
                throw new ArgumentException($"{nameof(sqsOptions.ConnectionString)} must be neither null nor empty", nameof(sqsOptions));
            if (string.IsNullOrEmpty(serviceId)) throw new ArgumentNullException(nameof(serviceId));
            this.loggerFactory = loggerFactory;
            this.serializationManager = serializationManager;
            _sqsOptions = sqsOptions;
            this.ServiceId = serviceId;
            Name = providerName;
            this.streamQueueMapper = streamQueueMapper;
        }

        public IQueueAdapterReceiver CreateReceiver(QueueId queueId)
        {
            return SQSAdapterReceiver.Create(serializationManager, loggerFactory, queueId, ServiceId, _sqsOptions);
        }

        public async Task QueueMessageBatchAsync<T>(Guid streamGuid, string streamNamespace, IEnumerable<T> events, StreamSequenceToken token, Dictionary<string, object> requestContext)
        {
            if (token != null)
            {
                throw new ArgumentException("SQSStream stream provider currently does not support non-null StreamSequenceToken.", nameof(token));
            }
            var queueId = streamQueueMapper.GetQueueForStream(streamGuid, streamNamespace);
            SQSStorage queue;
            if (!Queues.TryGetValue(queueId, out queue))
            {
                var tmpQueue = new SQSStorage(loggerFactory, queueId.ToString(), ServiceId, _sqsOptions);
                await tmpQueue.InitQueueAsync();
                queue = Queues.GetOrAdd(queueId, tmpQueue);
            }

            var msg = SQSBatchContainer.ToSQSMessage(this.serializationManager, streamGuid, streamNamespace, events, requestContext);
            if (queue.IsFifo)
            {
                msg.MessageGroupId = _sqsOptions.FifoMessageGroupId;
                if (!queue.UsesContentBasedDeduplication)
                {
                    msg.MessageDeduplicationId = _sqsOptions.FifoMessageDeduplicationIdGenerator?.Invoke();
                }
            }

            await queue.AddMessage(msg);
        }
    }
}
