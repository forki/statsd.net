namespace statsd.net
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks.Dataflow;
    using statsd.net.Configuration;
    using statsd.net.core;
    using statsd.net.core.Backends;
    using statsd.net.core.Structures;
    using statsd.net.Framework;
    using statsd.net.Logging;
    using statsd.net.shared;
    using statsd.net.shared.Factories;
    using statsd.net.shared.Listeners;
    using statsd.net.shared.Messages;
    using statsd.net.shared.Services;
    using TinyIoC;

    public class Statsd
    {
        private readonly TransformBlock<string, StatsdMessage> _messageParser;
        private readonly StatsdMessageRouterBlock _router;
        private readonly BroadcastBlock<Bucket> _messageBroadcaster;
        private readonly List<IBackend> _backends;
        private List<IListener> _listeners;
        private readonly CancellationTokenSource _tokenSource;
        private readonly ManualResetEvent _shutdownComplete;
        private static readonly ILog _log = LogProvider.GetLogger("statsd.net");

        public WaitHandle ShutdownWaitHandle => _shutdownComplete;

        public Statsd(string serviceName = null)
        {
            _log.Info("statsd.net starting.");
            _tokenSource = new CancellationTokenSource();
            _shutdownComplete = new ManualResetEvent(false);

            var systemInfoService = new SystemInfoService();

            var container = TinyIoCContainer.Current;
            container.Register<ISystemInfoService, SystemInfoService>(systemInfoService);

            
            serviceName = serviceName ?? systemInfoService.HostName;
            var systemMetricsService = new SystemMetricsService("statsdnet", serviceName);

            container.Register<ISystemMetricsService, SystemMetricsService>(systemMetricsService);

            /**
             * The flow is:
             *  Listeners ->
             *    Message Parser ->
             *      router ->
             *        Aggregator ->
             *          Broadcaster ->
             *            Backends
             */

            // Initialise the core blocks
            _router = new StatsdMessageRouterBlock();
            _messageParser = MessageParserBlockFactory.CreateMessageParserBlock(_tokenSource.Token, container.Resolve<ISystemMetricsService>());
            _messageParser.LinkTo(_router);
            _messageParser.Completion.LogAndContinueWith("MessageParser", () =>
              {
                  _log.Info("MessageParser: Completion signaled. Notifying the MessageBroadcaster.");
                  _messageBroadcaster.Complete();
              });
            _messageBroadcaster = new BroadcastBlock<Bucket>(Bucket.Clone);
            _messageBroadcaster.Completion.LogAndContinueWith("MessageBroadcaster", () =>
              {
                  _log.Info("MessageBroadcaster: Completion signaled. Notifying all backends.");
                  _backends.ForEach(q => q.Complete());
              });

            // Add the broadcaster to the IOC container
            container.Register<BroadcastBlock<Bucket>>(_messageBroadcaster);
            systemMetricsService.SetTarget(_messageBroadcaster);

            _backends = new List<IBackend>();
            _listeners = new List<IListener>();
        }

        public Statsd(StatsdnetConfiguration config)
            : this(config.Name)
        {
            _log.Info("statsd.net loading config.");
            var systemMetrics = TinyIoCContainer.Current.Resolve<ISystemMetricsService>();
            systemMetrics.HideSystemStats = config.HideSystemStats;

            LoadBackends(config, systemMetrics);

            // Load Aggregators
            var intervalServices = new List<IIntervalService>();
            var intervalService = new IntervalService(config.FlushInterval,
              _tokenSource.Token);
            intervalServices.Add(intervalService);
            LoadAggregators(config,
              intervalService,
              _messageBroadcaster,
              systemMetrics);
            // Load Listeners
            LoadListeners(config, _tokenSource.Token, systemMetrics);

            // Now start the interval service
            intervalServices.ForEach(p => p.Start());

            // Announce that we've started
            systemMetrics.LogCount("started");
        }

        private void LoadAggregators(StatsdnetConfiguration config,
          IntervalService intervalService,
          BroadcastBlock<Bucket> messageBroadcaster,
          ISystemMetricsService systemMetrics)
        {
            foreach (var aggregator in config.Aggregators)
            {
                switch (aggregator.Key)
                {
                    case "counters":
                        var counter = aggregator.Value as CounterAggregationConfig;
                        AddAggregator(MessageType.Counter,
                          TimedCounterAggregatorBlockFactory.CreateBlock(messageBroadcaster,
                            counter.Namespace,
                            intervalService),
                          systemMetrics);
                        break;
                    case "gauges":
                        var gauge = aggregator.Value as GaugeAggregatorConfig;
                        AddAggregator(MessageType.Gauge,
                          TimedGaugeAggregatorBlockFactory.CreateBlock(messageBroadcaster,
                            gauge.Namespace,
                            gauge.RemoveZeroGauges,
                            intervalService),
                          systemMetrics);
                        break;
                    case "calendargrams":
                        var calendargram = aggregator.Value as CalendargramAggregationConfig;
                        AddAggregator(MessageType.Calendargram,
                            TimedCalendargramAggregatorBlockFactory.CreateBlock(messageBroadcaster,
                                calendargram.Namespace,
                                intervalService,
                                new TimeWindowService()),
                                systemMetrics);
                        break;
                    case "timers":
                        var timer = aggregator.Value as TimersAggregationConfig;
                        AddAggregator(MessageType.Timing,
                          TimedLatencyAggregatorBlockFactory.CreateBlock(messageBroadcaster,
                            timer.Namespace,
                            intervalService,
                            timer.CalculateSumSquares),
                          systemMetrics);
                        // Add Percentiles
                        foreach (var percentile in timer.Percentiles)
                        {
                            AddAggregator(MessageType.Timing,
                              TimedLatencyPercentileAggregatorBlockFactory.CreateBlock(messageBroadcaster,
                                timer.Namespace,
                                intervalService,
                                percentile.Threshold,
                                percentile.Name),
                              systemMetrics);
                        }
                        break;

                }
            }
            // Add the Raw (pass-through) aggregator
            AddAggregator(MessageType.Raw,
              PassThroughBlockFactory.CreateBlock(messageBroadcaster, intervalService),
              systemMetrics);
        }

        private void LoadBackends(StatsdnetConfiguration config, ISystemMetricsService systemMetrics)
        {
            foreach (var backend in config.GetConfiguredBackends(systemMetrics))
            {
                AddBackend(backend, systemMetrics, backend.Name);
            }
        }

        private void LoadListeners(StatsdnetConfiguration config, 
            CancellationToken cancellationToken,
            ISystemMetricsService systemMetrics)
        {
            // Load listeners - done last and once the rest of the chain is in place
            foreach (var listenerConfig in config.Listeners)
            {
                if (listenerConfig is UDPListenerConfiguration)
                {
                    var udpConfig = listenerConfig as UDPListenerConfiguration;
                    AddListener(new UdpStatsListener(udpConfig.Port, systemMetrics));
                    systemMetrics.LogCount("startup.listener.udp." + udpConfig.Port);
                }
                else if (listenerConfig is TCPListenerConfiguration)
                {
                    var tcpConfig = listenerConfig as TCPListenerConfiguration;
                    AddListener(new TcpStatsListener(tcpConfig.Port, systemMetrics));
                    systemMetrics.LogCount("startup.listener.tcp." + tcpConfig.Port);
                }
                else if (listenerConfig is HTTPListenerConfiguration)
                {
                    var httpConfig = listenerConfig as HTTPListenerConfiguration;
                    AddListener(new HttpStatsListener(httpConfig.Port, systemMetrics));
                    systemMetrics.LogCount("startup.listener.http." + httpConfig.Port);
                }
                else if (listenerConfig is StatsdnetListenerConfiguration)
                {
                    var statsdnetConfig = listenerConfig as StatsdnetListenerConfiguration;
                    AddListener(new StatsdnetTcpListener(statsdnetConfig.Port, systemMetrics));
                    systemMetrics.LogCount("startup.listener.statsdnet." + statsdnetConfig.Port);
                }
                else if (listenerConfig is MSSQLRelayListenerConfiguration)
                {
                    var mssqlRelayConfig = listenerConfig as MSSQLRelayListenerConfiguration;
                    AddListener(new MSSQLRelayListener(mssqlRelayConfig.ConnectionString,
                        mssqlRelayConfig.PollInterval,
                        cancellationToken,
                        mssqlRelayConfig.BatchSize,
                        mssqlRelayConfig.DeleteAfterSend,
                        systemMetrics));
                }
            }
        }

        public void AddListener(IListener listener)
        {
            _log.InfoFormat("Adding listener {0}", listener.GetType().Name);
            _listeners.Add(listener);
            listener.LinkTo(_messageParser, _tokenSource.Token);
        }

        private void AddAggregator(MessageType targetType,
          ActionBlock<StatsdMessage> aggregator,
          ISystemMetricsService systemMetrics)
        {
            _router.AddTarget(targetType, aggregator);
            systemMetrics.LogCount("startup.aggregator." + targetType.ToString());
        }

        public void AddBackend(IBackend backend, ISystemMetricsService systemMetrics, string name)
        {
            _log.InfoFormat("Adding backend {0} named '{1}'", backend.GetType().Name, name);
            _backends.Add(backend);
            _messageBroadcaster.LinkTo(backend);
            backend.Completion.LogAndContinueWith(name, () =>
              {
                  if (_backends.All(q => !q.IsActive))
                  {
                      _shutdownComplete.Set();
                  }
              });
            systemMetrics.LogCount("startup.backend." + name);
        }

        public void Stop()
        {
            _tokenSource.Cancel();
            _shutdownComplete.WaitOne();
        }
    }
}
