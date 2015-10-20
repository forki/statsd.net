

namespace statsd.net.Backends
{
  using System;
  using System.ComponentModel.Composition;
  using System.Linq;
  using System.Net.Sockets;
  using System.Text;
  using System.Threading.Tasks;
  using System.Threading.Tasks.Dataflow;
  using System.Xml.Linq;
  using statsd.net.core;
  using statsd.net.core.Backends;
  using statsd.net.core.Messages;
  using statsd.net.core.Structures;
  using statsd.net.Logging;
  using statsd.net.shared;

  [Export(typeof (IBackend))]
  public class GraphiteTCPBackend : IBackend
  {
    private const int BATCH_SIZE = 100;

    private TcpClient _client;
    private ISystemMetricsService _systemMetrics;
    private readonly ILog _log = LogProvider.GetCurrentClassLogger();
    private BatchBlock<GraphiteLine> _batchBlock;
    private ActionBlock<GraphiteLine[]> _senderBlock;
    private string _host;
    private int _port;

    public string Name => "graphite-tcp";

    public bool IsActive { get; private set; }

    public int OutputCount => 0;

    public void Configure(string collectorName, XElement configElement, ISystemMetricsService systemMetrics)
    {
      _systemMetrics = systemMetrics;
      Completion = new Task(() => { IsActive = false; });
      _batchBlock = new BatchBlock<GraphiteLine>(BATCH_SIZE);
      _senderBlock = new ActionBlock<GraphiteLine[]>(batch => SendBatch(batch), Utility.OneAtATimeExecution());
      _batchBlock.LinkTo(_senderBlock);
      IsActive = true;

      _host = configElement.Attribute("host").Value;
      _port = configElement.ToInt("port");
    }

    public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, Bucket messageValue,
      ISourceBlock<Bucket> source, bool consumeToAccept)
    {
      messageValue.FeedTarget(_batchBlock);
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

    private void SendBatch(GraphiteLine[] batch)
    {
      try
      {
        _systemMetrics.LogCount("backends.graphite-tcp.write.attempt");
        EnsureConnectedClient(ref _client);
        var lines = batch.Select(p => p.ToString()).ToArray();
        var payload = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetBytes(payload);
        _systemMetrics.LogCount("backends.graphite-tcp.lines", lines.Length);
        _systemMetrics.LogCount("backends.graphite-tcp.bytes", bytes.Length);

        _client.GetStream().Write(bytes, 0, bytes.Length);
        _systemMetrics.LogCount("backends.graphite-tcp.write.success");
      }
      catch (Exception ex)
      {
        _log.ErrorException(string.Format("Could not write batch to graphite host at {0}:{1}", _host, _port), ex);
        _systemMetrics.LogCount("backends.graphite-tcp.write.failure");
        _systemMetrics.LogCount("backends.graphite-tcp.write.exception." + ex.GetType().Name);
      }
    }

    private void EnsureConnectedClient(ref TcpClient client)
    {
      if (client == null)
      {
        client = new TcpClient(_host, _port);
      }
      if (!client.Connected)
      {
        try
        {
          client.Close();
        }
        catch (Exception)
        {
          /* eat it */
        }
        client = new TcpClient();
        client.Connect(_host, _port);
      }
    }
  }
}