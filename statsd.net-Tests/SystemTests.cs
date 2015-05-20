using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace statsd.net_Tests
{
  [TestClass]
  public class SystemTests : StatsdTestSuite
  {
    [TestMethod]
    [Timeout(10 * 1000)] /* 10 seconds */
    public void SystemStartsWithNoTraffic_ShutsDown()
    {
      _statsd.Stop();
    }
  }
}
