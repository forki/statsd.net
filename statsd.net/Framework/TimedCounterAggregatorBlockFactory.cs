namespace statsd.net.Framework
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading.Tasks.Dataflow;
    using statsd.net.core.Structures;
    using statsd.net.Logging;
    using statsd.net.shared.Messages;
    using statsd.net.shared.Services;
    using statsd.net.shared.Structures;

  public class TimedCounterAggregatorBlockFactory
  {
      private static readonly ILog log = LogProvider.GetCurrentClassLogger();

    public static ActionBlock<StatsdMessage> CreateBlock(ITargetBlock<Bucket> target,
      string rootNamespace, 
      IIntervalService intervalService)
    {
      var counters = new ConcurrentDictionary<string, double>();
      var root = rootNamespace;
      var ns = String.IsNullOrEmpty(rootNamespace) ? "" : (rootNamespace + ".");

      var incoming = new ActionBlock<StatsdMessage>(p =>
        {
          var counter = p as Counter;
          counters.AddOrUpdate(counter.Name, counter.Value, (key, oldValue) => oldValue + counter.Value);
        },
        new ExecutionDataflowBlockOptions() { MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded });

      intervalService.Elapsed += (sender, e) =>
        {
          if (counters.Count == 0)
          {
            return;
          }

          var bucket = new CounterBucket(counters.ToArray(), e.Epoch, ns);
          counters.Clear();
          target.Post(bucket);
        };

      incoming.Completion.ContinueWith(p =>
        {
          log.Info("TimedCounterAggregatorBlock completing.");
          // Tell the upstream block that we're done
          target.Complete();
        });
      return incoming;
    }
  }
}
