namespace statsd.net
{
    using System.IO;
    using System.Reflection;
    using statsd.net.Configuration;
    using statsd.net.shared;
    using Topshelf;

  public class ServiceWrapper : ServiceControl
  {
    private Statsd _statsd;
    private readonly string _configFile;
    private StatsdnetConfiguration _config;

    public ServiceWrapper()
    {
      _configFile = "statsdnet.config";
    }

    public bool Start(HostControl host)
    {
        StartStatsD(false);
        return true;
    }

    public bool Stop(HostControl host)
    {
      if ( _statsd != null )
      {
        _statsd.Stop();
        _statsd.ShutdownWaitHandle.WaitOne();
      }

      if (_config != null)
      {
        _config.Dispose();
        _config = null;
      }

      return true;
    }

    public void StartStatsD(bool waitForCompletion = true)
    {
      //TODO : JV IS CONFIG FILE A ACTUAL FILE PATH?  IF SO THEN ITS MISLEADING SHOULD BE CONFIGFILEPATH??
      LoggingBootstrap.Configure();

      var configFile = ResolveConfigFile(_configFile);
      if (!File.Exists(configFile))
      {
        throw new FileNotFoundException("Could not find the statsd.net config file. I looked here: " + configFile);
      }
      _config = ConfigurationFactory.Parse(configFile);
      _statsd = new Statsd(_config);
      if (waitForCompletion)
      {
        _statsd.ShutdownWaitHandle.WaitOne();
      }
    }

    private static string ResolveConfigFile(string filename)
    {
      return Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), filename);
    }
  }
}
