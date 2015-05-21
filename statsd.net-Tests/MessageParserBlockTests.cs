using System;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using NSubstitute;
using Shouldly;
using statsd.net.core;
using statsd.net.shared.Factories;
using statsd.net.shared.Messages;
using Xunit;

namespace statsd.net_Tests
{

  public class MessageParserBlockTests
  {
    private TransformBlock<string, StatsdMessage> _block;
    private ISystemMetricsService _systemMetrics;

    public MessageParserBlockTests()
    {
      _systemMetrics = Substitute.For<ISystemMetricsService>();
      _block = MessageParserBlockFactory.CreateMessageParserBlock(new CancellationToken(), 
        _systemMetrics);
    }

    [Fact]
    public void ProcessedALine_IncrementedCounter()
    {
      _systemMetrics.When(p => p.LogCount("parser.linesSeen", 1));

      _block.Post(new Counter("foo", 1).ToString());
      _block.WaitUntilAllItemsProcessed();
      _systemMetrics.ReceivedCalls();
    }

    [Fact]
    public void ProcessedABadLine_IncrementedBadLineCounter()
    {
        _systemMetrics.When(p => p.LogCount("parser.badLinesSeen", 1));

      _block.Post("a bad line");
      _block.WaitUntilAllItemsProcessed();

      _systemMetrics.ReceivedCalls();
    }

    [Fact]
    public void ProcessedRawLine_GotValidRawMessageInstance()
    {
        _systemMetrics.When(p => p.LogCount("parser.LinesSeen", 1));

      var timestamp = DateTime.Now.Ticks;
      var metric = "a.raw.metric:100|r|" + timestamp;
      _block.Post(metric);
      var message = _block.Receive();

      message.ToString().ShouldBe(metric);
      _systemMetrics.ReceivedCalls();
    }

    [Fact]
    public void ProcessedRawLine_NoTimeStamp_GotValidRawMessageInstance()
    {
        _systemMetrics.When(p => p.LogCount("parser.LinesSeen", 1));

      var timestamp = DateTime.Now.Ticks;
      var metric = "a.raw.metric:100|r";
      _block.Post(metric);
      var message = _block.Receive();

      message.ToString().ShouldBe(metric);
      _systemMetrics.ReceivedCalls();
    }
  }
}
