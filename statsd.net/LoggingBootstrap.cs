namespace statsd.net
{
    using System;
    using System.IO;
    using Serilog;
        
    public static class LoggingBootstrap
    {
        public static void Configure()
        {
            var logFileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "StatsdNet", "StatsdNet.log");
            var logLayout = "{Timestamp:HH:mm} [{Level}] ({ThreadId}) {Message}{NewLine}{Exception}";

            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithProcessId()
                .Enrich.WithThreadId()
                .Enrich.With<EventTypeEnricher>()
                .WriteTo.Console(outputTemplate: logLayout)
                .WriteTo.File(logFileName, outputTemplate: logLayout)
                .CreateLogger();
        }
    }
}
