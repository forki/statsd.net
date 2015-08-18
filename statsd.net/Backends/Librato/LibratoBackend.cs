namespace statsd.net.Backends.Librato
{
    using System;
    using System.ComponentModel.Composition;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Dataflow;
    using System.Xml.Linq;
    using Jil;
    using Polly;
    using statsd.net.Configuration;
    using statsd.net.core;
    using statsd.net.core.Backends;
    using statsd.net.core.Structures;
    using statsd.net.Logging;
    using statsd.net.shared;
    using statsd.net.shared.Structures;

  /**
   * Flow of data:
   *  
   *   Bucket ->
   *      Preprocessor ->
   *         Batch Block ->
   *            Post to Librato 
   */
  [Export(typeof(IBackend))]
  public class LibratoBackend : IBackend
  {
    public const string ILLEGAL_NAME_CHARACTERS = @"[^-.:_\w]+";
    public const string LIBRATO_API_URL = "https://metrics-api.librato.com";

    private Task _completionTask;
    private readonly ILog _log = LogProvider.GetCurrentClassLogger();
    private string _serviceVersion;
    public bool IsActive { get; private set; }
    private ActionBlock<Bucket> _preprocessorBlock;
    private BatchBlock<LibratoMetric> _batchBlock;
    private ActionBlock<LibratoMetric[]> _outputBlock;
    private HttpClient _client;
    private ISystemMetricsService _systemMetrics;
    private int _pendingOutputCount;
    private Policy _retryPolicy;
    //private Incremental _retryStrategy;
    private LibratoBackendConfiguration _config;
    private string _source;

    public int OutputCount
    {
      get { return _pendingOutputCount; }
    }

    public string Name { get { return "Librato"; } }  
    
    public void Configure(string collectorName, XElement configElement, ISystemMetricsService systemMetrics)
    {
      _completionTask = new Task(() => IsActive = false);
      _systemMetrics = systemMetrics;

      var config = new LibratoBackendConfiguration(
          email: configElement.Attribute("email").Value,
          token: configElement.Attribute("token").Value,
          numRetries: configElement.ToInt("numRetries"),
          retryDelay: Utility.ConvertToTimespan(configElement.Attribute("retryDelay").Value),
          postTimeout: Utility.ConvertToTimespan(configElement.Attribute("postTimeout").Value),
          maxBatchSize: configElement.ToInt("maxBatchSize"),
          countersAsGauges: configElement.ToBoolean("countersAsGauges")
        );
      
      _config = config;
      _source = collectorName;
      _serviceVersion = Assembly.GetEntryAssembly().GetName().Version.ToString();

      _preprocessorBlock = new ActionBlock<Bucket>(bucket => ProcessBucket(bucket), Utility.UnboundedExecution());
      _batchBlock = new BatchBlock<LibratoMetric>(_config.MaxBatchSize);
      _outputBlock = new ActionBlock<LibratoMetric[]>(lines => PostToLibrato(lines), Utility.OneAtATimeExecution());
      _batchBlock.LinkTo(_outputBlock);

      _client = new HttpClient() { BaseAddress = new Uri(LIBRATO_API_URL) };

      var authByteArray = Encoding.ASCII.GetBytes(string.Format("{0}:{1}", _config.Email, _config.Token));

      _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authByteArray));
      _client.Timeout = TimeSpan.FromMilliseconds(_config.PostTimeout.TotalMilliseconds);
      

      _retryPolicy = Policy.Handle<TimeoutException>().WaitAndRetry(_config.NumRetries, retryAttempt => _config.RetryDelay, (exception, timeSpan) => 
      {
          _log.WarnException(String.Format("Retry failed. Trying again. Delay {1}, Error: {2}", timeSpan, exception.Message), exception);
          _systemMetrics.LogCount("backends.librato.retry");
      });

      IsActive = true;
    }

    public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader,
      Bucket messageValue,
      ISourceBlock<Bucket> source,
      bool consumeToAccept)
    {
      _preprocessorBlock.Post(messageValue);
      return DataflowMessageStatus.Accepted;
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

    private void ProcessBucket(Bucket bucket)
    {
      switch (bucket.BucketType)
      {
        case BucketType.Count:
          var counterBucket = bucket as CounterBucket;
          foreach (var count in counterBucket.Items)
          {
            if (_config.CountersAsGauges)
            {
              _batchBlock.Post(new LibratoGauge(counterBucket.RootNamespace + count.Key, count.Value, bucket.Epoch));
            }
            else
            {
              _batchBlock.Post(new LibratoCounter(counterBucket.RootNamespace + count.Key, count.Value, bucket.Epoch));
            }
          }
          break;
        case BucketType.Gauge:
          var gaugeBucket = bucket as GaugesBucket;
          foreach (var gauge in gaugeBucket.Gauges)
          {
            _batchBlock.Post(new LibratoGauge(gaugeBucket.RootNamespace + gauge.Key, gauge.Value, bucket.Epoch));
          }
          break;
        case BucketType.Timing:
          var timingBucket = bucket as LatencyBucket;
          foreach (var timing in timingBucket.Latencies)
          {
            _batchBlock.Post(new LibratoTiming(timingBucket.RootNamespace + timing.Key,
              timing.Value.Count,
              timing.Value.Sum,
              timing.Value.SumSquares,
              timing.Value.Min,
              timing.Value.Max,
              bucket.Epoch));
          }
          break;
        case BucketType.Percentile:
          var percentileBucket = bucket as PercentileBucket;
          double percentileValue;
          foreach (var pair in percentileBucket.Timings)
          {
            if (percentileBucket.TryComputePercentile(pair, out percentileValue))
            {
              _batchBlock.Post(new LibratoGauge(percentileBucket.RootNamespace + pair.Key + percentileBucket.PercentileName,
                percentileValue,
                bucket.Epoch));
            }
          }
          break;
      }
    }

    private void PostToLibrato(LibratoMetric[] lines)
    {
      try
      {
        PostToLibratoInternal(lines);
      }
      catch (Exception ex)
      {
        _log.ErrorException("Failed to post metrics to Librato.com", ex);
        _systemMetrics.LogCount("backends.librato.post.error." + ex.GetType().Name);
      }
    }

    private void PostToLibratoInternal(LibratoMetric[] lines)
    {
      var pendingLines = 0;
      foreach (var epochGroup in lines.GroupBy(p => p.Epoch))
      {
        var payload = GetPayload(epochGroup);
        pendingLines = payload.gauges.Length + payload.counters.Length;
        _systemMetrics.LogGauge("backends.librato.lines", pendingLines);
        Interlocked.Add(ref _pendingOutputCount, pendingLines);

        var request = new HttpRequestMessage();
        request.Headers.Add("User-Agent", "statsd.net-librato-backend/" + _serviceVersion);

        var content = new StringContent(JSON.Serialize(payload), Encoding.UTF8, "application/json");

        _retryPolicy.Execute(() =>
          {
            bool succeeded = false;
            try
            {
              _systemMetrics.LogCount("backends.librato.post.attempt");

              var task = _client.PostAsync("/v1/metrics", content);
              var response = task.Result;
              
              if (response.StatusCode == HttpStatusCode.Unauthorized)
              {
                _systemMetrics.LogCount("backends.librato.error.unauthorised");
                throw new UnauthorizedAccessException("Librato.com reports that your access is not authorised. Is your API key and email address correct?");
              }
              else if (response.StatusCode != HttpStatusCode.OK)
              {
                _systemMetrics.LogCount("backends.librato.error." + response.StatusCode);
                throw new Exception(String.Format("Request could not be processed. Server said {0}", response.StatusCode));
              }
              else
              {
                succeeded = true;
                _log.Info(String.Format("Wrote {0} lines to Librato.", pendingLines));
              }
            }
            finally
            {
              Interlocked.Add(ref _pendingOutputCount, -pendingLines);
              _systemMetrics.LogCount("backends.librato.post." + (succeeded ? "success" : "failure"));
            }
          });
      }
    }

    private APIPayload GetPayload(IGrouping<long, LibratoMetric> epochGroup)
    {
      var lines = epochGroup.ToList();
      // Split the lines up into gauges and counters
      var gauges = lines.Where(p => p.MetricType == LibratoMetricType.Gauge || p.MetricType == LibratoMetricType.Timing).ToArray();
      var counts = lines.Where(p => p.MetricType == LibratoMetricType.Counter).ToArray();

      var payload = new APIPayload();
      payload.gauges = gauges;
      payload.counters = counts;
      payload.measure_time = epochGroup.Key;
      payload.source = _source;
      return payload;
    }
  
  }
}
