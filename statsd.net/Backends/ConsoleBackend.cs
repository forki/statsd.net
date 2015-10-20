using System;

namespace statsd.net.Backends
{
  using System.ComponentModel.Composition;
  using System.Threading.Tasks;
  using System.Threading.Tasks.Dataflow;
  using System.Xml.Linq;
  using statsd.net.core;
  using statsd.net.core.Backends;
  using statsd.net.core.Structures;

  [Export(typeof (IBackend))]
  public class ConsoleBackend : IBackend
  {
    public string Name => "Console";

    public void Configure(string collectorName, XElement configElement, ISystemMetricsService systemMetrics)
    {
      IsActive = true;
      Completion = new Task(() => { IsActive = false; });
    }

    public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, Bucket bucket,
      ISourceBlock<Bucket> source, bool consumeToAccept)
    {
      Console.WriteLine(bucket.ToString());
      return DataflowMessageStatus.Accepted;
    }

    public void Complete()
    {
      Console.WriteLine("Done");
      Completion.Start();
    }

    public Task Completion { get; private set; }

    public void Fault(Exception exception)
    {
      throw new NotImplementedException();
    }

    public bool IsActive { get; private set; }

    public int OutputCount => 0;
  }
}