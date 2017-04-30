using System;
using System.Net;
using NUnit.Framework;
using DNSProtocol;

namespace DNSResolver
{
    /**
     * An IClock implementation that allows us to tweak the current time.
     */
    class TestClock: IClock
    {
        public long Time { get; set; }

        public TestClock(long start_time)
        {
            Time = start_time;
        }

        public void Advance(long amount)
        {
            Time += amount;
        }
    }

    [TestFixture]
    public class DNSCacheTest
    {
        private static int MAX_CACHE_SIZE = 2;

        /*
         * Creates a new clock (set to 0), and a resolver that is attuned to it.
         */
        private static Tuple<TestClock, ResolverCache> CreateCache()
        {
            var clock = new TestClock(0);
            return Tuple.Create(clock, new ResolverCache(clock, MAX_CACHE_SIZE));
        }

        [Test]
        public void testInitialCacheIsEmpty()
        {
            var clock_cache = CreateCache();
            var cache = clock_cache.Item2;

            var results = cache.Query(new Domain("example.com"),
                                      AddressClass.UNSUPPORTED,
                                      ResourceRecordType.UNSUPPORTED);

            Assert.That(results.Count, Is.EqualTo(0));
        }

        [Test]
        public void testQueryCacheWildcards()
        {
            var clock_cache = CreateCache();
            var cache = clock_cache.Item2;

            var a_resource = new AResource();
            a_resource.Address = IPAddress.Parse("192.168.0.1");

            var a_record = new DNSRecord();
            a_record.Name = new Domain("example.com");
            a_record.AddressClass = AddressClass.INTERNET;
            a_record.TimeToLive = 10;
            a_record.Resource = a_resource;

            var ns_resource = new NSResource();
            ns_resource.Nameserver = new Domain("dns.example.com");

            var ns_record = new DNSRecord();
            ns_record.Name = new Domain("example.com");
            ns_record.AddressClass = AddressClass.INTERNET;
            ns_record.TimeToLive = 10;
            ns_record.Resource = ns_resource;

            cache.Add(a_record);
            cache.Add(ns_record);

            var expected = new DNSRecord[] { a_record, ns_record };
            var results = cache.Query(new Domain("example.com"),
                                      AddressClass.UNSUPPORTED,
                                      ResourceRecordType.UNSUPPORTED);

            Assert.That(results, Is.EquivalentTo(expected));
        }

        [Test]
        public void testQueryCacheSpecific()
        {
            var clock_cache = CreateCache();
            var cache = clock_cache.Item2;

            var a_resource = new AResource();
            a_resource.Address = IPAddress.Parse("192.168.0.1");

            var a_record = new DNSRecord();
            a_record.Name = new Domain("example.com");
            a_record.AddressClass = AddressClass.INTERNET;
            a_record.TimeToLive = 10;
            a_record.Resource = a_resource;

            var ns_resource = new NSResource();
            ns_resource.Nameserver = new Domain("dns.example.com");

            var ns_record = new DNSRecord();
            ns_record.Name = new Domain("example.com");
            ns_record.AddressClass = AddressClass.INTERNET;
            ns_record.TimeToLive = 10;
            ns_record.Resource = ns_resource;

            cache.Add(a_record);
            cache.Add(ns_record);

            var expected = new DNSRecord[] { a_record };
            var results = cache.Query(new Domain("example.com"),
                                      AddressClass.INTERNET,
                                      ResourceRecordType.HOST_ADDRESS);

            Assert.That(results, Is.EquivalentTo(expected));
        }

        [Test]
        public void testQueryCacheTimeout()
        {
            var clock_cache = CreateCache();
            var cache = clock_cache.Item2;
            var clock = clock_cache.Item1;

            var a_resource = new AResource();
            a_resource.Address = IPAddress.Parse("192.168.0.1");

            var a_record = new DNSRecord();
            a_record.Name = new Domain("example.com");
            a_record.AddressClass = AddressClass.INTERNET;
            a_record.TimeToLive = 100;
            a_record.Resource = a_resource;

            var ns_resource = new NSResource();
            ns_resource.Nameserver = new Domain("dns.example.com");

            var ns_record = new DNSRecord();
            ns_record.Name = new Domain("example.com");
            ns_record.AddressClass = AddressClass.INTERNET;
            ns_record.TimeToLive = 10;
            ns_record.Resource = ns_resource;

            cache.Add(a_record);
            cache.Add(ns_record);
            clock.Advance(50); // > ns_ttl, < a_ttl

            var expected = new DNSRecord[] { a_record };
            var results = cache.Query(new Domain("example.com"),
                                      AddressClass.UNSUPPORTED,
                                      ResourceRecordType.UNSUPPORTED);

            Assert.That(results, Is.EquivalentTo(expected));
        }

        [Test]
        public void testQueryCacheEnforcesMaxSize()
        {
            var clock_cache = CreateCache();
            var cache = clock_cache.Item2;
            var clock = clock_cache.Item1;

            var a_resource = new AResource();
            a_resource.Address = IPAddress.Parse("192.168.0.1");

            var a_record = new DNSRecord();
            a_record.Name = new Domain("example.com");
            a_record.AddressClass = AddressClass.INTERNET;
            a_record.TimeToLive = 100; // > ns_ttl
            a_record.Resource = a_resource;

            var ns_resource = new NSResource();
            ns_resource.Nameserver = new Domain("dns.example.com");

            var ns_record = new DNSRecord();
            ns_record.Name = new Domain("example.com");
            ns_record.AddressClass = AddressClass.INTERNET;
            ns_record.TimeToLive = 10; // Smallest ttl, will be prunekd
            ns_record.Resource = ns_resource;

            var cname_resource = new CNAMEResource();
            cname_resource.Alias = new Domain("www.example.com");

            var cname_record = new DNSRecord();
            cname_record.Name = new Domain("example.com");
            cname_record.AddressClass = AddressClass.INTERNET;
            cname_record.TimeToLive = 50; // > ns_ttl
            cname_record.Resource = cname_resource;

            cache.Add(a_record);
            cache.Add(ns_record);
            cache.Add(cname_record);

            var expected = new DNSRecord[] { a_record, cname_record };
            var results = cache.Query(new Domain("example.com"),
                                      AddressClass.UNSUPPORTED,
                                      ResourceRecordType.UNSUPPORTED);

            Assert.That(results, Is.EquivalentTo(expected));
        }
    }
}
