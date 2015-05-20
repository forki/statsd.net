namespace statsd.net
{
  using Topshelf;

  public class Program
  {
    public static void Main(string[] args)
    {
        HostFactory.New(x => 
        {
            x.Service<ServiceWrapper>();
            x.RunAsLocalSystem();
            x.StartAutomatically();
            x.SetServiceName("Statsd.net");
            x.SetDisplayName("Statsd.net");
            x.SetDescription("Data collection and aggregation service for Graphite");
        });
    }
  }
}
