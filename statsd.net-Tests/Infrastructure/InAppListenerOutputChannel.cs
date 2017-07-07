namespace statsd.net_Tests.Infrastructure
{
    using System.Threading.Tasks;
    using StatsdClient;

    internal class InAppListenerOutputChannel : IOutputChannel
    {
        private readonly InAppListener _listener;

        public InAppListenerOutputChannel(InAppListener listener)
        {
            _listener = listener;
        }

        public void Send(string line)
        {
            _listener.Send(line);
        }

        public Task SendAsync(string line)
        {
            _listener.Send(line);
            return Task.FromResult(0);
        }
    }
}