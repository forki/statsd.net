using System;
using System.Linq;
using System.Threading.Tasks.Dataflow;
using log4net;
using NSubstitute;
using Shouldly;
using statsd.net.Framework;
using statsd.net.shared.Messages;
using statsd.net_Tests.Infrastructure;
using Xunit;

namespace statsd.net_Tests
{ 
  public class TimedLatencyPercentileAggregatorBlockTests
  {
    private ActionBlock<StatsdMessage> _block;
    private ControllableIntervalService _intervalService;
    private BucketOutputBlock _outputBuffer;
    private ILog _log;

    public TimedLatencyPercentileAggregatorBlockTests()
    {
      _intervalService = new ControllableIntervalService();
      _outputBuffer = new BucketOutputBlock();
      _log = Substitute.For<ILog>();
    }

    [Fact]
    public void WriteOneHunderedLatencies_p50PercentileLogged_Success()
    {
      _block = TimedLatencyPercentileAggregatorBlockFactory.CreateBlock(_outputBuffer,
        String.Empty,
        _intervalService,
        50,
        null,
        _log);

      TestUtility.Range(100, false).ForEach(p => _block.Post(new Timing("foo", p)));
      _block.WaitUntilAllItemsProcessed();
      _intervalService.Pulse();
      _outputBuffer["foo.p50"].ShouldBe(50);
    }

    [Fact]
    public void WriteOneHunderedLatencies_p90PercentileLogged_Success()
    {
      _block = TimedLatencyPercentileAggregatorBlockFactory.CreateBlock(_outputBuffer,
        String.Empty,
        _intervalService,
        90,
        null,
        _log);

      TestUtility.Range(100, false).ForEach(p => _block.Post(new Timing("foo", p)));
      _block.WaitUntilAllItemsProcessed();
      _intervalService.Pulse();

      _outputBuffer["foo.p90"].ShouldBe(90);
    }

    [Fact]
    public void WriteFourLatencies_PercentileLogged_Success()
    {
      _block = TimedLatencyPercentileAggregatorBlockFactory.CreateBlock(_outputBuffer,
        String.Empty,
        _intervalService,
        90,
        null,
        _log);

      _block.Post(new Timing("foo", 100));
      _block.Post(new Timing("foo", 200));
      _block.Post(new Timing("foo", 300));
      _block.Post(new Timing("foo", 400));
      _block.WaitUntilAllItemsProcessed();
      _intervalService.Pulse();

      _outputBuffer.GraphiteLines.Any(p => p.Name == "foo.p90").ShouldBe(true);
      _outputBuffer["foo.p90"].ShouldBe(400);
    }

    [Fact]
    public void WriteLatenciesToTwoBuckets_MeasurementsSeparate_Success()
    {
      _block = TimedLatencyPercentileAggregatorBlockFactory.CreateBlock(_outputBuffer,
        String.Empty,
        _intervalService,
        80,
        null,
        _log);
      var pulseDate = DateTime.Now;

      // Bucket one
      TestUtility.Range(5, false).ForEach(p => _block.Post(new Timing("foo", p * 100)));
      // Bucket two
      TestUtility.Range(5, false).ForEach(p => _block.Post(new Timing("bar", p * 100)));
      _block.WaitUntilAllItemsProcessed();
      _intervalService.Pulse(pulseDate);

      _outputBuffer["foo.p80"].ShouldBe(400);
      _outputBuffer["bar.p80"].ShouldBe(400);
      _outputBuffer.Items.Count.ShouldBe(1);
    }
  }
}
