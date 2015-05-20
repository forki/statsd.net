# statsd.net
A high-performance stats collection service based on [etsy's](http://etsy.com/) [statsd service](https://github.com/etsy/statsd/) and written in c#.net.

## Key Features
* Enables multiple latency buckets for the same metric to measure things like p90/5min and p90/1hour in one go
* Can receive stats over UDP, TCP and HTTP.
* Compatible with etsy's statds protocol for sending counts, timings, latencies and sets
* Supports [librato.com](http://metrics.librato.com/) as a backend
* Supports writing out to another statsd.net instance for relay configurations

## Installation, Guidance, Configuration and Reference Information
* Find all this and more on the **[statsd.net wiki](https://github.com/lukevenediger/statsd.net/tree/master/statsd.net)**

## Coming Soon
* More backends
* Web-based management console with a RESTful API
* Histogram stats
* Calendargram stats - easily count unique values according to calendar buckets

### Licence
MIT Licence.
