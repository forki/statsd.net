using System;
using System.ComponentModel.Composition;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Xml.Linq;
using log4net;
using statsd.net.Configuration;
using statsd.net.core;
using statsd.net.core.Backends;
using statsd.net.core.Messages;
using statsd.net.core.Structures;
using statsd.net.shared;

namespace statsd.net.Backends.InfluxDB
{
    [Export(typeof(IBackend))]
    public class InfluxDbBackend : IBackend
    {
        private ILog _log;

        private bool _isActive;
        public string Name { get { return "InfluxDB"; } }

        private Task _completionTask;
        private BatchBlock<GraphiteLine> _batchBlock;
        private ActionBlock<GraphiteLine[]> _actionBlock;
        private ISystemMetricsService _systemMetrics;

        public bool IsActive
        {
            get { return _isActive; }
        }

        public void Configure(string collectorName, XElement configElement, ISystemMetricsService systemMetrics)
        {
            _log = SuperCheapIOC.Resolve<ILog>();
            _systemMetrics = systemMetrics;

        }

        public int OutputCount
        {
            get { return _batchBlock.OutputCount; }
        }

        public void Complete()
        {
            _completionTask.Start();
        }

        public Task Completion
        {
            get { return _completionTask; }
        }

        public void Fault(Exception exception)
        {
            throw new NotImplementedException();
        }

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, Bucket messageValue, ISourceBlock<Bucket> source, bool consumeToAccept)
        {
            //messageValue.FeedTarget(_batchBlock);
            return DataflowMessageStatus.Accepted;
        }
    }
}
