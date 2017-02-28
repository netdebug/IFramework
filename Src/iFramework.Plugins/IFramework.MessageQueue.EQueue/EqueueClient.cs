﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using EQueueMessages = EQueue.Protocols;
using IFramework.Message;
using IFramework.MessageQueue.EQueue.MessageFormat;
using IFramework.Config;
using IFramework.Infrastructure;
using System.Threading;
using IFramework.Infrastructure.Logging;
using IFramework.IoC;
using IFramework.Message.Impl;

namespace IFramework.MessageQueue.EQueue
{
    public class EQueueClient : IMessageQueueClient
    {
        public string ClusterName { get; set; }
        public List<System.Net.IPEndPoint> NameServerList { get; set; }
        protected List<EQueueConsumer> _subscriptionClients;
        protected List<EQueueConsumer> _queueConsumers;
        protected ILogger _logger = null;

      
        public EQueueProducer _producer { get; protected set; }


        public EQueueClient(string clusterName, List<System.Net.IPEndPoint> nameServerList)
        {
            ClusterName = clusterName;
            NameServerList = nameServerList;
            _subscriptionClients = new List<EQueueConsumer>();
            _queueConsumers = new List<EQueueConsumer>();
            _logger = IoCFactory.Resolve<ILoggerFactory>().Create(this.GetType().Name);
            _producer = new EQueueProducer(ClusterName, NameServerList);
            _producer.Start();
        }

        public void Dispose()
        {
            _producer.Stop();
        }

        protected EQueueMessages.Message GetEQueueMessage(IMessageContext messageContext, string topic)
        {
            topic = Configuration.Instance.FormatMessageQueueName(topic);
            var jsonValue = ((MessageContext)messageContext).EqueueMessage.ToJson();
            return new EQueueMessages.Message(topic, 1, Encoding.UTF8.GetBytes(jsonValue));
        }

        public void Publish(IMessageContext messageContext, string topic)
        {
            _producer.Send(GetEQueueMessage(messageContext, topic), messageContext.Key);
        }

        public void Send(IMessageContext messageContext, string queue)
        {
            _producer.Send(GetEQueueMessage(messageContext, queue), messageContext.Key);
        }

        public ICommitOffsetable StartQueueClient(string commandQueueName, string consumerId, OnMessagesReceived onMessagesReceived, int fullLoadThreshold = 1000, int waitInterval = 1000)
        {
            commandQueueName = Configuration.Instance.FormatMessageQueueName(commandQueueName);
            consumerId = Configuration.Instance.FormatMessageQueueName(consumerId);
            var queueConsumer = CreateQueueConsumer(commandQueueName, consumerId, fullLoadThreshold, waitInterval, onMessagesReceived);
            _queueConsumers.Add(queueConsumer);
            return queueConsumer;
        }



        public ICommitOffsetable StartSubscriptionClient(string topic, string subscriptionName, string consumerId, OnMessagesReceived onMessagesReceived, int fullLoadThreshold = 1000, int waitInterval = 1000)
        {
            topic = Configuration.Instance.FormatMessageQueueName(topic);
            subscriptionName = Configuration.Instance.FormatMessageQueueName(subscriptionName);
            var subscriptionClient = CreateSubscriptionClient(topic, subscriptionName, onMessagesReceived, 
                                                              consumerId, fullLoadThreshold, waitInterval);
            _subscriptionClients.Add(subscriptionClient);
            return subscriptionClient;
        }

        public IMessageContext WrapMessage(object message, string correlationId = null, string topic = null, string key = null, string replyEndPoint = null, string messageId = null, SagaInfo sagaInfo = null)
        {
            var messageContext = new MessageContext(message, messageId);
            if (!string.IsNullOrEmpty(correlationId))
            {
                messageContext.CorrelationID = correlationId;
            }
            if (!string.IsNullOrEmpty(topic))
            {
                messageContext.Topic = topic;
            }
            if (!string.IsNullOrEmpty(key))
            {
                messageContext.Key = key;
            }
            if (!string.IsNullOrEmpty(replyEndPoint))
            {
                messageContext.ReplyToEndPoint = replyEndPoint;
            }
            if (sagaInfo != null)
            {
                messageContext.SagaInfo = sagaInfo;
            }
            return messageContext;
        }

        #region private methods

        OnEQueueMessageReceived BuildOnEQueueMessageReceived(OnMessagesReceived onMessagesReceived)
        {
            return (consumer, message) =>
            {
                var equeueMessage = Encoding.UTF8.GetString(message.Body).ToJsonObject<EQueueMessage>();
                var messageContext = new MessageContext(equeueMessage, message.QueueId, message.QueueOffset);
                onMessagesReceived(messageContext);
            };
        }
       



        EQueueConsumer CreateSubscriptionClient(string topic, string subscriptionName, OnMessagesReceived onMessagesReceived, string consumerId = null, int fullLoadThreshold = 1000, int waitInterval = 1000)
        {
            var consumer = new EQueueConsumer(ClusterName, NameServerList, topic, subscriptionName, consumerId,
                                              BuildOnEQueueMessageReceived(onMessagesReceived),
                                              fullLoadThreshold, waitInterval);
            return consumer;
        }

        EQueueConsumer CreateQueueConsumer(string commandQueueName, string consumerId, int fullLoadThreshold, int waitInterval, OnMessagesReceived onMessagesReceived)
        {
            var consumer = new EQueueConsumer(ClusterName, NameServerList, commandQueueName, 
                                              commandQueueName, consumerId,
                                              BuildOnEQueueMessageReceived(onMessagesReceived),
                                              fullLoadThreshold, waitInterval);
            return consumer;
        }
        #endregion
    }
}
