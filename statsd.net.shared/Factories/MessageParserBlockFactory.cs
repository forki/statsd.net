namespace statsd.net.shared.Factories
{
    using System;
    using System.Threading;
    using System.Threading.Tasks.Dataflow;
    using statsd.net.core;
    using statsd.net.shared.Logging;
    using statsd.net.shared.Messages;

  public static class MessageParserBlockFactory
  {
    private static readonly ILog log = LogProvider.GetCurrentClassLogger();

    public static TransformBlock<String, StatsdMessage> CreateMessageParserBlock(CancellationToken cancellationToken,
      ISystemMetricsService systemMetrics)
    {
      var block = new TransformBlock<String, StatsdMessage>(
        (line) =>
        {
          systemMetrics.LogCount("parser.linesSeen");
          StatsdMessage message = StatsdMessageFactory.ParseMessage(line);
          if (message is InvalidMessage)
          {
            systemMetrics.LogCount("parser.badLinesSeen");
            log.Info("Bad message: " + ((InvalidMessage)message).Reason + Environment.NewLine + line);
          }
          return message;
        },
        new ExecutionDataflowBlockOptions()
        {
          MaxDegreeOfParallelism = ExecutionDataflowBlockOptions.Unbounded,
          CancellationToken = cancellationToken
        });
      return block;
    }
  }
}
