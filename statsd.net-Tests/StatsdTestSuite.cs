﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using statsd.net;
using statsd.net.core;
using statsd.net.core.Messages;
using statsd.net.shared;
using statsd.net_Tests.Infrastructure;

namespace statsd.net_Tests
{
  [TestClass]
  public abstract class StatsdTestSuite
  {
    protected Statsd _statsd;
    protected InAppListener _listener;
    protected InAppBackend _backend;
    protected StatsdClient.Statsd _client;
    protected ControllableIntervalService _intervalService;
    protected OutputBufferBlock<GraphiteLine> _outputBlock;
    protected ISystemMetricsService _systemMetrics;

    [TestInitialize]
    public void Setup()
    {
      _statsd = new Statsd();
      _listener = new InAppListener();
      _backend = new InAppBackend();
      _intervalService = new ControllableIntervalService();
      _outputBlock = new OutputBufferBlock<GraphiteLine>();
      _client = new StatsdClient.Statsd("", 0, outputChannel : new InAppListenerOutputChannel(_listener));
      _statsd.AddListener(_listener);
      _statsd.AddBackend(_backend, _systemMetrics, "testing");
      _systemMetrics = Substitute.For<ISystemMetricsService>();
    }

    [TestCleanup]
    public void Teardown()
    {
      _statsd.Stop();
    }
  }
}
