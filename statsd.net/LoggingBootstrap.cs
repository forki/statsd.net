namespace statsd.net
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using Serilog;
    using Serilog.Events;
        
    public static class LoggingBootstrap
    {
        public static void Configure()
        {
            var logFileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "StatsdNet", "StatsdNet.log");
            var logLayout = "{Timestamp:HH:mm} [{Level}] ({ThreadId}) {Message}{NewLine}{Exception}";

            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithProcessId()
                .Enrich.WithProcessId()
                .Enrich.WithThreadId()
                .WriteTo.ColoredConsole(outputTemplate: logLayout)
                .WriteTo.File(logFileName, outputTemplate: logLayout)
                .CreateLogger();
        }
    }
}
