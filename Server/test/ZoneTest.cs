using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Xml;
using NUnit.Framework;

using DNSProtocol;

namespace DNSServer
{
	[TestFixture]
	public class ZoneTest
	{
		private static DNSRecord start_of_authority = new DNSRecord(
			new Domain("example.com"),
			AddressClass.INTERNET,
			42,
			new SOAResource(
				new Domain("ns.example.com"),
				new Domain("mail.example.com"),
				42,
				3600,
				3600,
				3600,
				0));

		private static EndPoint[] relays = new EndPoint[0];

		[Test]
		public void TestAuthorityWithoutDelegation()
		{
			// Ensure that we are considered authoritative for things that
			// are in our zone (without involvement by subzones)
			var zone = new DNSZone(start_of_authority, relays);
			Assert.That(zone.IsAuthorityFor(new Domain("www.example.com")), Is.True);
		}

		[Test]
		public void TestNotAuthorityWithoutDelegation()
		{
			// Ensure that we are not considered authoritative for things clearly
			// outside of our zone
			var zone = new DNSZone(start_of_authority, relays);
			Assert.That(zone.IsAuthorityFor(new Domain("bogus.com")), Is.False);
		}

		[Test]
		public void TestNoSubzoneAuthoritative()
		{
			// Ensures that we don't return a subzone for things we're directly an
			// authority for
			var zone = new DNSZone(start_of_authority, relays);
			Assert.That(zone.FindSubZone(new Domain("www.example.com")), Is.Null);
		}

		[Test]
		public void TestNoSubzoneNotAuthoritatie()
		{
			// Ensures that we don't return a subzone for things that are outside of
			// our authority
			var zone = new DNSZone(start_of_authority, relays);
			Assert.That(zone.FindSubZone(new Domain("bogus.com")), Is.Null);
		}

		[Test]
		public void TestAuthorityWithDelegation()
		{
			// Ensures that we're not an authority for things that are subzones
			var zone = new DNSZone(start_of_authority, relays);
			var subzone = new DNSRecord(
				new Domain("foo.example.com"),
				AddressClass.INTERNET,
				42,
				new NSResource(new Domain("ns.foo.example.com")));
			zone.Add(subzone);

			Assert.That(zone.IsAuthorityFor(new Domain("www.foo.example.com")), Is.False);
		}

		[Test]
		public void TestSubzoneWithDelegation()
		{
			// Ensure that we can find the subzone for a domain in that subzone
			var zone = new DNSZone(start_of_authority, relays);
			var subzone = new DNSRecord(
				new Domain("foo.example.com"),
				AddressClass.INTERNET,
				42,
				new NSResource(new Domain("ns.foo.example.com")));
			zone.Add(subzone);

			Assert.That(zone.FindSubZone(new Domain("www.foo.example.com")),
						Is.EqualTo(new Domain("foo.example.com")));
		}

		[Test]
		public void TestAuthorityWhenEqualToDelegation()
		{
			// Ensure that we are an authority for the subzone's address itself (which can
			// occur if, say, there's a CNAME pointing to it)
			var zone = new DNSZone(start_of_authority, relays);
			var subzone = new DNSRecord(
				new Domain("foo.example.com"),
				AddressClass.INTERNET,
				42,
				new NSResource(new Domain("ns.foo.example.com")));
			zone.Add(subzone);

			Assert.That(zone.IsAuthorityFor(new Domain("foo.example.com")), Is.True);
		}

		[Test]
		public void TestQuery()
		{
			var zone = new DNSZone(start_of_authority, relays);
			var www_a_record = new DNSRecord(
				new Domain("www.example.com"),
				AddressClass.INTERNET,
				42,
				new AResource(IPAddress.Parse("192.168.0.1")));

			var www_a_record_2 = new DNSRecord(
				new Domain("www.example.com"),
				AddressClass.INTERNET,
				42,
				new AResource(IPAddress.Parse("192.168.0.2")));
			
			// Something that matches the class and record type, but not the domain
			var www2_a_record = new DNSRecord(
				new Domain("www2.example.com"),
				AddressClass.INTERNET,
				42,
				new AResource(IPAddress.Parse("192.168.0.3")));

			// Something that matches the domain and class, but not the record type
			var www_cname_record = new DNSRecord(
				new Domain("www.example.com"),
				AddressClass.INTERNET,
				42,
				new CNAMEResource(new Domain("www2.example.com")));

			zone.Add(www_a_record);
			zone.Add(www_a_record_2);
			zone.Add(www2_a_record);
			zone.Add(www_cname_record);

			var query_result = zone.Query(
				new Domain("www.example.com"),
				ResourceRecordType.HOST_ADDRESS,
				AddressClass.INTERNET);

			var expected = new List<DNSRecord>();
			expected.Add(www_a_record);
			expected.Add(www_a_record_2);
			Assert.That(query_result, Is.EquivalentTo(expected));
		}
	}

	[TestFixture]
	public class ZoneParserTest
	{
		private XmlDocument ParseXML(string config)
		{
			var doc = new XmlDocument();
			doc.LoadXml(config);
			return doc;
		}

		[Test]
		public void TestMinimumConfigFile()
		{
			// The smallest file that you can get away with is an empty zone, which has just an SOA
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""7200"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");

			var zone = DNSZone.Unserialize(config);
			Assert.That(zone.Relays, Is.Empty);

			Assert.That(zone.StartOfAuthority, Is.EqualTo(
				new DNSRecord(
					new Domain("example.com"),
					AddressClass.INTERNET,
					7200,
					new SOAResource(
						new Domain("ns.example.com"),
						new Domain("admin.example.com"),
						0,
						3600,
						60,
						3600,
						60))));
		}

		[Test]
		public void TestConfigARecord()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""7200"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />

    <A name=""www.example.com"" class=""IN"" ttl=""3600"" address=""192.168.0.1"" />
</zone>
			");

			var zone = DNSZone.Unserialize(config);

			var records = new List<DNSRecord>();
			records.Add(new DNSRecord(
				new Domain("www.example.com"),
				AddressClass.INTERNET,
				3600,
				new AResource(IPAddress.Parse("192.168.0.1"))));

			Assert.That(zone.Records, Is.EquivalentTo(records));
		}

		[Test]
		public void TestConfigCNAMERecord()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""7200"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />

    <CNAME name=""www.example.com"" class=""IN"" ttl=""3600"" alias=""web1.example.com"" />
</zone>
			");

			var zone = DNSZone.Unserialize(config);

			var records = new List<DNSRecord>();
			records.Add(new DNSRecord(
				new Domain("www.example.com"),
				AddressClass.INTERNET,
				3600,
				new CNAMEResource(new Domain("web1.example.com"))));

			Assert.That(zone.Records, Is.EquivalentTo(records));
		}

		[Test]
		public void TestConfigNSRecord()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""7200"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />

    <NS name=""example.com"" class=""IN"" ttl=""3600"" nameserver=""ns.example.com"" />
</zone>
			");

			var zone = DNSZone.Unserialize(config);

			var records = new List<DNSRecord>();
			records.Add(new DNSRecord(
				new Domain("example.com"),
				AddressClass.INTERNET,
				3600,
				new NSResource(new Domain("ns.example.com"))));

			Assert.That(zone.Records, Is.EquivalentTo(records));
		}

		[Test]
		public void TestConfigMXRecord()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""7200"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />

    <MX name=""example.com"" class=""IN"" ttl=""3600"" priority=""42"" mailserver=""smtp.example.com"" />
</zone>
			");

			var zone = DNSZone.Unserialize(config);

			var records = new List<DNSRecord>();
			records.Add(new DNSRecord(
				new Domain("example.com"),
				AddressClass.INTERNET,
				3600,
				new MXResource(42, new Domain("smtp.example.com"))));

			Assert.That(zone.Records, Is.EquivalentTo(records));
		}

		[Test]
		public void TestConfigRelay()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""7200"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />

	<relay address=""192.168.0.1"" port=""53"" />
</zone>
			");

			var zone = DNSZone.Unserialize(config);
			var relays = new EndPoint[] {
				new IPEndPoint(IPAddress.Parse("192.168.0.1"), 53)
			};

			Assert.That(zone.Relays, Is.EquivalentTo(relays));
		}

		[Test]
		public void TestEmptyConfigFileFails()
		{
			// The empty configuration file is not valid, since it lacks a SOA
			var config = ParseXML("<zone></zone>");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestMultipleSOAFails()
		{
			// Multiple SOA records are not allowed, since we currently don't support multiple zones
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""7200"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />

	<SOA name=""fabrikam.com"" class=""IN"" ttl=""7200"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestGarbageTTLFails()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""blargh"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestNegativeTTLFails()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""-42"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestHighTTLFails()
		{
			// TTLs are 16-bit numbers, so any TTL over 18.2 hours doesn't work
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""1000000"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestMissingTTLFails() 
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestInvalidClassFails()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""BLARGH"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestMissingClassFails()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestBadNameFails()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""...example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestMissingNameFails()
		{
			var config = ParseXML(@"
<zone>
	<SOA class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOABadPrimaryNS()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""....example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOAMissingPrimaryNS()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOABadHostmaster()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""......example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOAMissingHostmaster()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOAGarbageSerial()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         serial=""blargh"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOANegativeSerial()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         serial=""-42"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOATooHighSerial()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         serial=""1000000"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOAMissingSerial()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOAGarbageRefresh()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         serial=""0"" refresh=""blargh"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOANegativeRefresh()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         serial=""0"" refresh=""-42"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOATooHighRefresh()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         serial=""0"" refresh=""1000000"" retry=""60"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOAMissingRefresh()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         retry=""3600"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOAGarbageRetry()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         serial=""0"" refresh=""3600"" retry=""blargh"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOANegativeRetry()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         serial=""0"" refresh=""3600"" retry=""-42"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOATooHighRetry()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         serial=""0"" refresh=""3600"" retry=""1000000"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOAMissingRetry()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" expire=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOAGarbageExpire()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""blargh"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOANegativeExpire()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""-42"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOATooHighExpire()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""1000000"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOAMissingExpire()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""3600"" min-ttl=""60"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOAGarbageMinTTL()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""blargh"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOANegativeMinTTL()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""-42"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOATooHighMinTTL()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""1000000"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestSOAMissingMinTTL()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""3600"" expire=""3600"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestAGarbageAddress()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""3600"" expire=""3600"" />

	<A name=""example.com"" class=""IN"" ttl=""3600"" address=""blargh"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestAIPv6Address()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""3600"" expire=""3600"" />

	<A name=""example.com"" class=""IN"" ttl=""3600"" address=""2001:beef:cafe::1337"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestAMissingAddress()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""3600"" expire=""3600"" />

	<A name=""example.com"" class=""IN"" ttl=""3600"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestCNAMEGarbageAlias()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""3600"" expire=""3600"" />

	<CNAME name=""example.com"" class=""IN"" ttl=""3600"" alias=""...example.com"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}


		[Test]
		public void TestCNAMEMissingAlias()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""3600"" expire=""3600"" />

	<CNAME name=""example.com"" class=""IN"" ttl=""3600"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestNSGarbageNameserver()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""3600"" expire=""3600"" />

	<NS name=""example.com"" class=""IN"" ttl=""3600"" nameserver=""....example.com"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestNSMissingNameserver()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""3600"" expire=""3600"" />

	<NS name=""example.com"" class=""IN"" ttl=""3600"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestMXGarbagePriority()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""3600"" expire=""3600"" />

	<MX name=""example.com"" class=""IN"" ttl=""3600"" priority=""blargh"" mailserver=""smtp.example.com"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestMXNegativePriority()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""3600"" expire=""3600"" />

	<MX name=""example.com"" class=""IN"" ttl=""3600"" priority=""-42"" mailserver=""smtp.example.com"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestMXTooHighPriority()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""3600"" expire=""3600"" />

	<MX name=""example.com"" class=""IN"" ttl=""3600"" priority=""1000000"" mailserver=""smtp.example.com"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestMXMissingPriority()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""3600"" expire=""3600"" />

	<MX name=""example.com"" class=""IN"" ttl=""3600"" mailserver=""smtp.example.com"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestMXGarbageMailserver()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""3600"" expire=""3600"" />

	<MX name=""example.com"" class=""IN"" ttl=""3600"" priority=""1"" mailserver="".....example.com"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestMXMissingMailserver()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""3600"" 
         primary-ns=""ns.example.com"" hostmaster=""hostmaster.example.com""
         refresh=""3600"" retry=""3600"" expire=""3600"" />

	<MX name=""example.com"" class=""IN"" ttl=""3600"" priority=""1"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestRelayGarbageAddress()
		{
			// The smallest file that you can get away with is an empty zone, which has just an SOA
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""7200"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />

	<relay address=""blargh"" port=""53"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestRelayMissingAddress()
		{
			// The smallest file that you can get away with is an empty zone, which has just an SOA
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""7200"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />

	<relay port=""53"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestRelayGarbagePort()
		{
			// The smallest file that you can get away with is an empty zone, which has just an SOA
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""7200"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />

	<relay address=""192.168.0.1"" port=""blargh"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestRelayNegativePort()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""7200"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />

	<relay address=""192.168.0.1"" port=""-42"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestRelayTooHighPort()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""7200"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />

	<relay address=""192.168.0.1"" port=""1000000"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}

		[Test]
		public void TestRelayMissingPort()
		{
			var config = ParseXML(@"
<zone>
	<SOA name=""example.com"" class=""IN"" ttl=""7200"" 
         primary-ns=""ns.example.com"" hostmaster=""admin.example.com""
         serial=""0"" refresh=""3600"" retry=""60"" expire=""3600"" min-ttl=""60"" />

	<relay address=""192.168.0.1"" />
</zone>
			");
			Assert.Throws<InvalidDataException>(() => DNSZone.Unserialize(config));
		}
	}
}
