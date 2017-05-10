using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
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
	}
}
