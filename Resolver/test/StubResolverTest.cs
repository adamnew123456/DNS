using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;

using DNSProtocol;

namespace DNSResolver
{
	[TestFixture]
	public class StubResolverTest
	{
		[Test]
		public void TestStubResolverAcceptsARecords()
		{
			// The idea behind this test is to make sure that direct answers to the question are accpted
			// (no CNAMEs, no referrals, etc.). A records are the most common query, so they are a good
			// starting point.
			var relay = new IPEndPoint(IPAddress.Parse("192.168.0.1"), 53);

			var expected_question = new DNSQuestion(
				new Domain("example.com"),
				ResourceRecordType.HOST_ADDRESS,
				AddressClass.INTERNET);

			var expected_answer = new DNSRecord(
				new Domain("example.com"), AddressClass.INTERNET, 42,
				new AResource(IPv4Address.Parse("192.168.0.1")));

			var expected_packet = new DNSPacket(
				42, false, QueryType.STANDARD_QUERY, false, false, true, true, ResponseType.NO_ERROR,
				new DNSQuestion[] { expected_question }, new DNSRecord[] { expected_answer }, new DNSRecord[0], new DNSRecord[0]);


			Func<EndPoint, DNSQuestion, bool, DNSPacket> gives_direct_answers = (target, question, is_recursive) =>
			{
				Assert.That(target, Is.EqualTo(relay));
				Assert.That(question, Is.EqualTo(expected_question));
				return expected_packet;
			};

			var resolver = new StubResolver(new NoopCache(), gives_direct_answers);
			var result = resolver.Resolve(new Domain("example.com"),
										  ResourceRecordType.HOST_ADDRESS,
										  AddressClass.INTERNET,
										  new EndPoint[] { relay });

			var expected_result = new ResolverResult();
			expected_result.answers = new DNSRecord[] { expected_answer };
			expected_result.aliases = new DNSRecord[0];
			expected_result.referrals = new DNSRecord[0];
			expected_result.referral_additional = new DNSRecord[0];

			Assert.That(result, Is.EqualTo(expected_result));
		}

		[Test]
		public void TestStubResolverAcceptsSOARecords()
		{
			// The same as the previous test, but this makes sure that less common
			// record types are still recognized as answers for the appropriate queries
			var relay = new IPEndPoint(IPAddress.Parse("192.168.0.1"), 53);

			var expected_question = new DNSQuestion(
				new Domain("example.com"),
				ResourceRecordType.START_OF_AUTHORITY,
				AddressClass.INTERNET);

			var expected_answer = new DNSRecord(
				new Domain("example.com"), AddressClass.INTERNET, 42,
				new SOAResource(
					new Domain("ns.example.com"),
					new Domain("hostmaster.example.com"),
					0,
					360,
					360,
					360,
					360));

			var expected_packet = new DNSPacket(
				42, false, QueryType.STANDARD_QUERY, false, false, true, true, ResponseType.NO_ERROR,
				new DNSQuestion[] { expected_question }, new DNSRecord[] { expected_answer }, new DNSRecord[0], new DNSRecord[0]);


			Func<EndPoint, DNSQuestion, bool, DNSPacket> gives_direct_answers = (target, question, is_recursive) =>
			{
				Assert.That(target, Is.EqualTo(relay));
				Assert.That(question, Is.EqualTo(expected_question));
				return expected_packet;
			};

			var resolver = new StubResolver(new NoopCache(), gives_direct_answers);
			var result = resolver.Resolve(new Domain("example.com"),
										  ResourceRecordType.START_OF_AUTHORITY,
										  AddressClass.INTERNET,
										  new EndPoint[] { relay });

			var expected_result = new ResolverResult();
			expected_result.answers = new DNSRecord[] { expected_answer };
			expected_result.aliases = new DNSRecord[0];
			expected_result.referrals = new DNSRecord[0];
			expected_result.referral_additional = new DNSRecord[0];

			Assert.That(result, Is.EqualTo(expected_result));
		}

		[Test]
		public void TestStubResolverAcceptsAliases()
		{
			// Make sure that a single alias is supported
			var relay = new IPEndPoint(IPAddress.Parse("192.168.0.1"), 53);

			var expected_question = new DNSQuestion(
				new Domain("example.com"),
				ResourceRecordType.HOST_ADDRESS,
				AddressClass.INTERNET);

			var expected_alias_answer = new DNSRecord(
				new Domain("example.com"), AddressClass.INTERNET, 42,
				new CNAMEResource(new Domain("www.example.com")));

			var expected_addr_answer = new DNSRecord(
				new Domain("www.example.com"), AddressClass.INTERNET, 42,
				new AResource(IPv4Address.Parse("192.168.0.1")));

			var expected_packet = new DNSPacket(
				42, false, QueryType.STANDARD_QUERY, false, false, true, true, ResponseType.NO_ERROR,
				new DNSQuestion[] { expected_question },
				new DNSRecord[] { expected_alias_answer, expected_addr_answer },
				new DNSRecord[0], new DNSRecord[0]);


			Func<EndPoint, DNSQuestion, bool, DNSPacket> gives_direct_answers = (target, question, is_recursive) =>
			{
				Assert.That(target, Is.EqualTo(relay));
				Assert.That(question, Is.EqualTo(expected_question));
				return expected_packet;
			};

			var resolver = new StubResolver(new NoopCache(), gives_direct_answers);
			var result = resolver.Resolve(new Domain("example.com"),
										  ResourceRecordType.HOST_ADDRESS,
										  AddressClass.INTERNET,
										  new EndPoint[] { relay });

			var expected_result = new ResolverResult();
			expected_result.answers = new DNSRecord[] { expected_addr_answer };
			expected_result.aliases = new DNSRecord[] { expected_alias_answer };
			expected_result.referrals = new DNSRecord[0];
			expected_result.referral_additional = new DNSRecord[0];

			Assert.That(result, Is.EqualTo(expected_result));
		}

		[Test]
		public void TestStubResolverAcceptsSeveralAliases()
		{
			// Make sure that chains of aliases are supported
			var relay = new IPEndPoint(IPAddress.Parse("192.168.0.1"), 53);

			var expected_question = new DNSQuestion(
				new Domain("example.com"),
				ResourceRecordType.HOST_ADDRESS,
				AddressClass.INTERNET);

			var expected_alias_answer_1 = new DNSRecord(
				new Domain("example.com"), AddressClass.INTERNET, 42,
				new CNAMEResource(new Domain("www.example.com")));

			var expected_alias_answer_2 = new DNSRecord(
				new Domain("www.example.com"), AddressClass.INTERNET, 42,
				new CNAMEResource(new Domain("web1.www.example.com")));

			var expected_addr_answer = new DNSRecord(
				new Domain("web1.www.example.com"), AddressClass.INTERNET, 42,
				new AResource(IPv4Address.Parse("192.168.0.1")));

			var expected_packet = new DNSPacket(
				42, false, QueryType.STANDARD_QUERY, false, false, true, true, ResponseType.NO_ERROR,
				new DNSQuestion[] { expected_question },
				new DNSRecord[] { expected_alias_answer_1, expected_alias_answer_2, expected_addr_answer },
				new DNSRecord[0], new DNSRecord[0]);


			Func<EndPoint, DNSQuestion, bool, DNSPacket> gives_direct_answers = (target, question, is_recursive) =>
			{
				Assert.That(target, Is.EqualTo(relay));
				Assert.That(question, Is.EqualTo(expected_question));
				return expected_packet;
			};

			var resolver = new StubResolver(new NoopCache(), gives_direct_answers);
			var result = resolver.Resolve(new Domain("example.com"),
										  ResourceRecordType.HOST_ADDRESS,
										  AddressClass.INTERNET,
										  new EndPoint[] { relay });

			var expected_result = new ResolverResult();
			expected_result.answers = new DNSRecord[] { expected_addr_answer };
			expected_result.aliases = new DNSRecord[] { expected_alias_answer_1, expected_alias_answer_2 };
			expected_result.referrals = new DNSRecord[0];
			expected_result.referral_additional = new DNSRecord[0];

			Assert.That(result, Is.EqualTo(expected_result));
		}

		[Test]
		public void TestStubResolverReturnsRedirections()
		{
			// Make sure that any redirections are returned, even if there aren't any actual
			// results
			var relay = new IPEndPoint(IPAddress.Parse("192.168.0.1"), 53);

			var expected_question = new DNSQuestion(
				new Domain("example.com"),
				ResourceRecordType.HOST_ADDRESS,
				AddressClass.INTERNET);

			var expected_ns_answer = new DNSRecord(
				new Domain("example.com"), AddressClass.INTERNET, 42,
				new NSResource(new Domain("ns.example.com")));

			var expected_packet = new DNSPacket(
				42, false, QueryType.STANDARD_QUERY, false, false, true, true, ResponseType.NO_ERROR,
				new DNSQuestion[] { expected_question }, new DNSRecord[0], new DNSRecord[] { expected_ns_answer }, 
				new DNSRecord[0]);


			Func<EndPoint, DNSQuestion, bool, DNSPacket> gives_direct_answers = (target, question, is_recursive) =>
			{
				Assert.That(target, Is.EqualTo(relay));
				Assert.That(question, Is.EqualTo(expected_question));
				return expected_packet;
			};

			var resolver = new StubResolver(new NoopCache(), gives_direct_answers);
			var result = resolver.Resolve(new Domain("example.com"),
										  ResourceRecordType.HOST_ADDRESS,
										  AddressClass.INTERNET,
										  new EndPoint[] { relay });

			var expected_result = new ResolverResult();
			expected_result.answers = new DNSRecord[0];
			expected_result.aliases = new DNSRecord[0];
			expected_result.referrals = new DNSRecord[] { expected_ns_answer };
			expected_result.referral_additional = new DNSRecord[0];

			Assert.That(result, Is.EqualTo(expected_result));
		}

		[Test]
		public void TestStubResolverReturnsRedirectionsWithGlue()
		{
			// Make sure that any redirections are returned, even if there aren't any actual
			// results, along with their glue if provided
			var relay = new IPEndPoint(IPAddress.Parse("192.168.0.1"), 53);

			var expected_question = new DNSQuestion(
				new Domain("example.com"),
				ResourceRecordType.HOST_ADDRESS,
				AddressClass.INTERNET);

			var expected_ns_answer = new DNSRecord(
				new Domain("example.com"), AddressClass.INTERNET, 42,
				new NSResource(new Domain("ns.example.com")));

			var expected_glue_answer = new DNSRecord(
				new Domain("ns.example.com"), AddressClass.INTERNET, 42,
				new AResource(IPAddress.Parse("192.168.0.2")));

			var expected_packet = new DNSPacket(
				42, false, QueryType.STANDARD_QUERY, false, false, true, true, ResponseType.NO_ERROR,
				new DNSQuestion[] { expected_question }, new DNSRecord[0], new DNSRecord[] { expected_ns_answer }, 
				new DNSRecord[] { expected_glue_answer });


			Func<EndPoint, DNSQuestion, bool, DNSPacket> gives_direct_answers = (target, question, is_recursive) =>
			{
				Assert.That(target, Is.EqualTo(relay));
				Assert.That(question, Is.EqualTo(expected_question));
				return expected_packet;
			};

			var resolver = new StubResolver(new NoopCache(), gives_direct_answers);
			var result = resolver.Resolve(new Domain("example.com"),
										  ResourceRecordType.HOST_ADDRESS,
										  AddressClass.INTERNET,
										  new EndPoint[] { relay });

			var expected_result = new ResolverResult();
			expected_result.answers = new DNSRecord[0];
			expected_result.aliases = new DNSRecord[0];
			expected_result.referrals = new DNSRecord[] { expected_ns_answer };
			expected_result.referral_additional = new DNSRecord[] { expected_glue_answer };

			Assert.That(result, Is.EqualTo(expected_result));
		}

		[Test]
		public void TestStubResolverRejectsIrrelevantDomains()
		{
			// We want to make sure that irrelevant records, that don't point to anything that we asked for
			var relay = new IPEndPoint(IPAddress.Parse("192.168.0.1"), 53);

			var expected_question = new DNSQuestion(
				new Domain("example.com"),
				ResourceRecordType.HOST_ADDRESS,
				AddressClass.INTERNET);

			var unexpected_answer = new DNSRecord(
				new Domain("bogus.com"), AddressClass.INTERNET, 42,
				new AResource(IPv4Address.Parse("192.168.0.1")));

			var expected_answer = new DNSRecord(
				new Domain("example.com"), AddressClass.INTERNET, 42,
				new AResource(IPv4Address.Parse("192.168.0.2")));

			var expected_packet = new DNSPacket(
				42, false, QueryType.STANDARD_QUERY, false, false, true, true, ResponseType.NO_ERROR,
				new DNSQuestion[] { expected_question }, new DNSRecord[] { unexpected_answer, expected_answer },
				new DNSRecord[0], new DNSRecord[0]);


			Func<EndPoint, DNSQuestion, bool, DNSPacket> gives_direct_answers = (target, question, is_recursive) =>
			{
				Assert.That(target, Is.EqualTo(relay));
				Assert.That(question, Is.EqualTo(expected_question));
				return expected_packet;
			};

			var resolver = new StubResolver(new NoopCache(), gives_direct_answers);
			var result = resolver.Resolve(new Domain("example.com"),
										  ResourceRecordType.HOST_ADDRESS,
										  AddressClass.INTERNET,
										  new EndPoint[] { relay });

			var expected_result = new ResolverResult();
			expected_result.answers = new DNSRecord[] { expected_answer };
			expected_result.aliases = new DNSRecord[0];
			expected_result.referrals = new DNSRecord[0];
			expected_result.referral_additional = new DNSRecord[0];

			Assert.That(result, Is.EqualTo(expected_result));
		}

		[Test]
		public void TestStubResolverRejectsIrrelevantGlue()
		{
			// Make sure that any redirections are returned, but rejects any bad glue
			var relay = new IPEndPoint(IPAddress.Parse("192.168.0.1"), 53);

			var expected_question = new DNSQuestion(
				new Domain("example.com"),
				ResourceRecordType.HOST_ADDRESS,
				AddressClass.INTERNET);

			var expected_ns_answer = new DNSRecord(
				new Domain("example.com"), AddressClass.INTERNET, 42,
				new NSResource(new Domain("ns.example.com")));

			var unexpected_glue_answer = new DNSRecord(
				new Domain("ns2.example.com"), AddressClass.INTERNET, 42,
				new AResource(IPv4Address.Parse("192.168.0.2")));

			var expected_packet = new DNSPacket(
				42, false, QueryType.STANDARD_QUERY, false, false, true, true, ResponseType.NO_ERROR,
				new DNSQuestion[] { expected_question }, new DNSRecord[0], new DNSRecord[] { expected_ns_answer },
				new DNSRecord[] { unexpected_glue_answer });


			Func<EndPoint, DNSQuestion, bool, DNSPacket> gives_direct_answers = (target, question, is_recursive) =>
			{
				Assert.That(target, Is.EqualTo(relay));
				Assert.That(question, Is.EqualTo(expected_question));
				return expected_packet;
			};

			var resolver = new StubResolver(new NoopCache(), gives_direct_answers);
			var result = resolver.Resolve(new Domain("example.com"),
										  ResourceRecordType.HOST_ADDRESS,
										  AddressClass.INTERNET,
										  new EndPoint[] { relay });

			var expected_result = new ResolverResult();
			expected_result.answers = new DNSRecord[0];
			expected_result.aliases = new DNSRecord[0];
			expected_result.referrals = new DNSRecord[] { expected_ns_answer };
			expected_result.referral_additional = new DNSRecord[0];

			Assert.That(result, Is.EqualTo(expected_result));
		}

		[Test]
		public void TestStubResolverRecordsCNAMEAnswers()
		{
			// This ensures that any request for a CNAME to a paritcular domain ends up in the answers
			// list (and only the answers list) if we're asking for CNAMEs
			var relay = new IPEndPoint(IPAddress.Parse("192.168.0.1"), 53);

			var expected_question = new DNSQuestion(
				new Domain("example.com"),
				ResourceRecordType.CANONICAL_NAME,
				AddressClass.INTERNET);

			var expected_answer = new DNSRecord(
				new Domain("example.com"), AddressClass.INTERNET, 42,
				new CNAMEResource(new Domain("www.example.com")));

			var expected_packet = new DNSPacket(
				42, false, QueryType.STANDARD_QUERY, false, false, true, true, ResponseType.NO_ERROR,
				new DNSQuestion[] { expected_question }, new DNSRecord[] { expected_answer }, new DNSRecord[0], new DNSRecord[0]);


			Func<EndPoint, DNSQuestion, bool, DNSPacket> gives_direct_answers = (target, question, is_recursive) =>
			{
				Assert.That(target, Is.EqualTo(relay));
				Assert.That(question, Is.EqualTo(expected_question));
				return expected_packet;
			};

			var resolver = new StubResolver(new NoopCache(), gives_direct_answers);
			var result = resolver.Resolve(new Domain("example.com"),
										  ResourceRecordType.CANONICAL_NAME,
										  AddressClass.INTERNET,
										  new EndPoint[] { relay });

			var expected_result = new ResolverResult();
			expected_result.answers = new DNSRecord[] { expected_answer };
			expected_result.aliases = new DNSRecord[0];
			expected_result.referrals = new DNSRecord[0];
			expected_result.referral_additional = new DNSRecord[0];

			Assert.That(result, Is.EqualTo(expected_result));
		}

		[Test]
		public void TestStubResolverSequenceOfRelays()
		{
			// Make sure that the resolver ignores failing resolvers, as long as at least one succeeds
			var good_relay = new IPEndPoint(IPAddress.Parse("192.168.0.1"), 53);
			var bad_relay = new IPEndPoint(IPAddress.Parse("192.168.0.2"), 53);

			var expected_question = new DNSQuestion(
				new Domain("example.com"),
				ResourceRecordType.HOST_ADDRESS,
				AddressClass.INTERNET);

			var expected_answer = new DNSRecord(
				new Domain("example.com"), AddressClass.INTERNET, 42,
				new AResource(IPv4Address.Parse("192.168.0.1")));

			var expected_packet = new DNSPacket(
				42, false, QueryType.STANDARD_QUERY, false, false, true, true, ResponseType.NO_ERROR,
				new DNSQuestion[] { expected_question }, new DNSRecord[] { expected_answer }, new DNSRecord[0], new DNSRecord[0]);

			Func<EndPoint, DNSQuestion, bool, DNSPacket> gives_direct_answers = (target, question, is_recursive) =>
			{
				if (target.Equals(bad_relay))
				{
					throw new SocketException();
				}

				Assert.That(target, Is.EqualTo(good_relay));
				Assert.That(question, Is.EqualTo(expected_question));
				return expected_packet;
			};

			var resolver = new StubResolver(new NoopCache(), gives_direct_answers);
			var result = resolver.Resolve(new Domain("example.com"),
										  ResourceRecordType.HOST_ADDRESS,
										  AddressClass.INTERNET,
										  new EndPoint[] { bad_relay, good_relay });

			var expected_result = new ResolverResult();
			expected_result.answers = new DNSRecord[] { expected_answer };
			expected_result.aliases = new DNSRecord[0];
			expected_result.referrals = new DNSRecord[0];
			expected_result.referral_additional = new DNSRecord[0];

			Assert.That(result, Is.EqualTo(expected_result));
		}
	}
}
