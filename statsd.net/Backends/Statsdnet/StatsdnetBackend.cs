using System;

namespace statsd.net.Backends.Statsdnet
{
  using System.Collections.Generic;
  using System.ComponentModel.Composition;
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;
  using System.Threading.Tasks.Dataflow;
  using System.Xml.Linq;
  using statsd.net.core;
  using statsd.net.core.Backends;
  using statsd.net.core.Messages;
  using statsd.net.core.Structures;
  using statsd.net.Configuration;
  using statsd.net.Logging;
  using statsd.net.shared;
  using statsd.net.shared.Blocks;

  /// <summary>
  /// Forwards all metrics on to another statsd.net instance over TCP.
  /// </summary>
  [Export(typeof (IBackend))]
  public class StatsdnetBackend : IBackend
  {
    private readonly ILog log = LogProvider.GetCurrentClassLogger();

    private TimedBufferBlock<GraphiteLine[]> _bufferBlock;
    private ISystemMetricsService _systemMetrics;
    private StatsdnetForwardingClient _client;

    public string Name => "Statsdnet";

    public void Configure(string collectorName, XElement configElement, ISystemMetricsService systemMetrics)
    {
      _systemMetrics = systemMetrics;

      var config = new StatsdBackendConfiguration(configElement.Attribute("host").Value,
        configElement.ToInt("port"),
        Utility.ConvertToTimespan(configElement.Attribute("flushInterval").Value),
        configElement.ToBoolean("enableCompression", true));

      _client = new StatsdnetForwardingClient(config.Host, config.Port, _systemMetrics);
      _bufferBlock = new TimedBufferBlock<GraphiteLine[]>(config.FlushInterval, PostMetrics);

      Completion = new Task(() => { IsActive = false; });

      IsActive = true;
    }

    private void PostMetrics(GraphiteLine[][] lineArrays)
    {
      var lines = new List<GraphiteLine>();
      foreach (var graphiteLineArray in lineArrays)
      {
        lines.AddRange(graphiteLineArray);
      }
      var rawText = string.Join(Environment.NewLine,
        lines.Select(line => line.ToString()).ToArray());
      var bytes = Encoding.UTF8.GetBytes(rawText);
      if (_client.Send(bytes))
      {
        _systemMetrics.LogCount("backends.statsdnet.lines", lines.Count);
        _systemMetrics.LogGauge("backends.statsdnet.bytes", bytes.Length);
      }
    }

    public bool IsActive { get; private set; }

    public int OutputCount => 0;

    public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader,
      Bucket messageValue,
      ISourceBlock<Bucket> source,
      bool consumeToAccept)
    {
      var lines = messageValue.ToLines();
      _bufferBlock.Post(lines);

      return DataflowMessageStatus.Accepted;
    }

    public void Complete()
    {
      Completion.Start();
    }

    public Task Completion { get; private set; }

    public void Fault(Exception exception)
    {
      throw new NotImplementedException();
    }
  }
}