﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using IFramework.Infrastructure.Logging;
using IFramework.IoC;
using Kafka.Client.Cfg;
using Kafka.Client.Cluster;
using Kafka.Client.Requests;
using Kafka.Client.Utils;
using Kafka.Client.ZooKeeperIntegration;

namespace Kafka.Client.Consumers
{
    /// <summary>
    ///     Background thread worker class that is used to fetch data from a single broker
    /// </summary>
    internal class FetcherRunnable
    {
        public static ILogger Logger = IoCFactory.Resolve<ILoggerFactory>().Create(typeof(FetcherRunnable));

        private readonly Broker _broker;
        private readonly ConsumerConfiguration _config;
        private readonly int _fetchBufferLength;
        private readonly Action<PartitionTopicInfo> _markPartitonWithError;
        private readonly string _name;
        private readonly IList<PartitionTopicInfo> _partitionTopicInfos;
        private readonly IConsumer _simpleConsumer;
        private readonly IZooKeeperClient _zkClient;
        private volatile bool _shouldStop;

        internal FetcherRunnable(string name,
                                 IZooKeeperClient zkClient,
                                 ConsumerConfiguration config,
                                 Broker broker,
                                 List<PartitionTopicInfo> partitionTopicInfos,
                                 Action<PartitionTopicInfo> markPartitonWithError)
        {
            _name = name;
            _zkClient = zkClient;
            _config = config;
            _broker = broker;
            _partitionTopicInfos = partitionTopicInfos;
            _fetchBufferLength = config.MaxFetchBufferLength;
            _markPartitonWithError = markPartitonWithError;
            _simpleConsumer = new Consumer(_config, broker.Host, broker.Port);
        }

        /// <summary>
        ///     Method to be used for starting a new thread
        /// </summary>
        internal void Run()
        {
            foreach (var partitionTopicInfo in _partitionTopicInfos)
            {
                Logger.InfoFormat("{0} start fetching topic: {1} part: {2} offset: {3} from {4}:{5}",
                                  _name,
                                  partitionTopicInfo.Topic,
                                  partitionTopicInfo.PartitionId,
                                  partitionTopicInfo.NextRequestOffset,
                                  _broker.Host,
                                  _broker.Port);
            }

            var reqId = 0;
            while (!_shouldStop && _partitionTopicInfos.Any())
            {
                try
                {
                    var fetchablePartitionTopicInfos =
                        _partitionTopicInfos.Where(pti => pti.GetMessagesCount() < _fetchBufferLength);

                    long read = 0;

                    if (fetchablePartitionTopicInfos.Any())
                    {
                        var builder =
                            new FetchRequestBuilder().CorrelationId(reqId)
                                                     .ClientId(_config.ConsumerId ?? _name)
                                                     .MaxWait(_config.MaxFetchWaitMs)
                                                     .MinBytes(_config.FetchMinBytes);
                        fetchablePartitionTopicInfos.ForEach(pti => builder.AddFetch(pti.Topic, pti.PartitionId,
                                                                                     pti.NextRequestOffset, _config.FetchSize));

                        var fetchRequest = builder.Build();
                        Logger.Debug("Sending fetch request: " + fetchRequest);
                        var response = _simpleConsumer.Fetch(fetchRequest);
                        Logger.Debug("Fetch request completed");
                        var partitonsWithErrors = new List<PartitionTopicInfo>();
                        foreach (var partitionTopicInfo in fetchablePartitionTopicInfos)
                        {
                            var messages =
                                response.MessageSet(partitionTopicInfo.Topic, partitionTopicInfo.PartitionId);
                            switch (messages.ErrorCode)
                            {
                                case (short) ErrorMapping.UnknownTopicOrPartitionCode:
                                case (short) ErrorMapping.NotLeaderForPartitionCode:
                                    Logger.ErrorFormat(
                                                       "Error returned from broker {2} for partition {0} : KafkaErrorCode: {1}",
                                                       partitionTopicInfo.PartitionId, messages.ErrorCode,
                                                       partitionTopicInfo.BrokerId);
                                    break;
                                case (short) ErrorMapping.NoError:
                                    var bytesRead = partitionTopicInfo.Add(messages);
                                    // TODO: The highwater offset on the message set is the end of the log partition. If the message retrieved is -1 of that offset, we are at the end.
                                    if (messages.Messages.Any())
                                    {
                                        partitionTopicInfo.NextRequestOffset = messages.Messages.Last().Offset + 1;
                                        read += bytesRead;
                                    }
                                    else
                                    {
                                        Logger.DebugFormat("No message returned by FetchRequest: {0}",
                                                           fetchRequest.ToString());
                                    }
                                    break;
                                case (short) ErrorMapping.OffsetOutOfRangeCode:
                                    try
                                    {
                                        Logger.InfoFormat("offset for {0} out of range", partitionTopicInfo);
                                        var resetOffset = ResetConsumerOffsets(partitionTopicInfo.Topic,
                                                                               partitionTopicInfo.PartitionId);
                                        if (resetOffset >= 0)
                                        {
                                            partitionTopicInfo.ResetOffset(resetOffset);
                                            Logger.InfoFormat("{0} marked as done.", partitionTopicInfo);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.ErrorFormat("Error getting offsets for partition {0} : {1}",
                                                           partitionTopicInfo.PartitionId, ex.FormatException());
                                        partitonsWithErrors.Add(partitionTopicInfo);
                                    }
                                    break;
                                default:
                                    Logger.ErrorFormat(
                                                       "Error returned from broker {2} for partition {0} : KafkaErrorCode: {1}",
                                                       partitionTopicInfo.PartitionId, messages.ErrorCode,
                                                       partitionTopicInfo.BrokerId);
                                    //Console.WriteLine("------------------------Error returned from broker {2} for partition {0} : KafkaErrorCode: {1}", partitionTopicInfo.PartitionId, messages.ErrorCode, partitionTopicInfo.BrokerId);
                                    partitonsWithErrors.Add(partitionTopicInfo);
                                    break;
                            }
                        }

                        reqId = reqId == int.MaxValue ? 0 : reqId + 1;
                        if (partitonsWithErrors.Any())
                        {
                            RemovePartitionsFromProessing(partitonsWithErrors);
                        }
                    }

                    if (read > 0)
                    {
                        Logger.Debug("Fetched bytes: " + read);
                    }

                    if (read == 0)
                    {
                        Logger.DebugFormat("backing off {0} ms", _config.BackOffIncrement);
                        Thread.Sleep(_config.BackOffIncrement);
                    }
                }
                catch (Exception ex)
                {
                    if (_shouldStop)
                    {
                        Logger.InfoFormat("FetcherRunnable {0} interrupted", this);
                    }
                    else
                    {
                        Logger.ErrorFormat("error in FetcherRunnable {0}", ex.FormatException());
                    }
                }
            }

            Logger.InfoFormat("stopping fetcher {0} to host {1}", _name, _broker.Host);
            //Console.WriteLine("------------------------stopping fetcher {0} to host {1}---------------------", _name, _broker.Host);
        }

        private void RemovePartitionsFromProessing(List<PartitionTopicInfo> partitonsWithErrors)
        {
            foreach (var partitonWithError in partitonsWithErrors)
            {
                _partitionTopicInfos.Remove(partitonWithError);
                _markPartitonWithError(partitonWithError);
            }
        }

        internal void Shutdown()
        {
            _shouldStop = true;
        }

        private long ResetConsumerOffsets(string topic, int partitionId)
        {
            long offset;
            switch (_config.AutoOffsetReset)
            {
                case OffsetRequest.SmallestTime:
                    offset = OffsetRequest.EarliestTime;
                    break;
                case OffsetRequest.LargestTime:
                    offset = OffsetRequest.LatestTime;
                    break;
                default:
                    return 0;
            }

            var requestInfo = new Dictionary<string, List<PartitionOffsetRequestInfo>>();
            requestInfo[topic] =
                new List<PartitionOffsetRequestInfo> {new PartitionOffsetRequestInfo(partitionId, offset, 1)};

            var request = new OffsetRequest(requestInfo);
            var offsets = _simpleConsumer.GetOffsetsBefore(request);
            var topicDirs = new ZKGroupTopicDirs(_config.GroupId, topic);
            var offsetFound = offsets.ResponseMap[topic].First().Offsets[0];
            Logger.InfoFormat("updating partition {0} with {1} offset {2}", partitionId,
                              offset == OffsetRequest.EarliestTime ? "earliest" : "latest", offsetFound);
            ZkUtils.UpdatePersistentPath(_zkClient, topicDirs.ConsumerOffsetDir + "/" + partitionId,
                                         offsetFound.ToString(CultureInfo.InvariantCulture));

            return offsetFound;
        }

        ~FetcherRunnable()
        {
            if (_simpleConsumer != null)
            {
                _simpleConsumer.Dispose();
            }
        }
    }
}