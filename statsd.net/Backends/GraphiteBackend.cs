using System;

namespace statsd.net.Backends
{
  using System.ComponentModel.Composition;
  using System.Net.Sockets;
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

  [Export(typeof (IBackend))]
  public class GraphiteBackend : IBackend
  {
    private UdpClient _client;
    private ISystemMetricsService _systemMetrics;
    private readonly ILog _log = LogProvider.GetCurrentClassLogger();
    private ActionBlock<GraphiteLine> _senderBlock;

    public string Name => "Graphite";

    public void Configure(string collectorName, XElement configElement, ISystemMetricsService systemMetrics)
    {
      _systemMetrics = systemMetrics;
      Completion = new Task(() => { IsActive = false; });
      _senderBlock = new ActionBlock<GraphiteLine>(message => SendLine(message), Utility.OneAtATimeExecution());
      IsActive = true;

      var config = new GraphiteConfiguration(configElement.Attribute("host").Value, configElement.ToInt("port"));

      var ipAddress = Utility.HostToIPv4Address(config.Host);
      _client = new UdpClient();
      _client.Connect(ipAddress, config.Port);
    }

    public bool IsActive { get; private set; }

    public int OutputCount => 0;

    public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, Bucket messageValue,
      ISourceBlock<Bucket> source, bool consumeToAccept)
    {
      messageValue.FeedTarget(_senderBlock);
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

    private void SendLine(GraphiteLine line)
    {
      byte[] data = Encoding.ASCII.GetBytes(line.ToString());
      try
      {
        _client.Send(data, data.Length);
        _systemMetrics.LogCount("backends.graphite.lines");
        _systemMetrics.LogCount("backends.graphite.bytes", data.Length);
      }
      catch (SocketException ex)
      {
        _log.Error("Failed to send packet to Graphite: " + ex.SocketErrorCode);
      }
    }
  }
}