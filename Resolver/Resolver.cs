using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;

using DNSProtocol;
using NLog;

namespace DNSResolver
{
	/**
     * Records errors that occur when resolving a domain..
     */
	public class ResolverException : Exception
	{
		public ResolverException(Domain domain, string message)
			: base("When resolving " + domain + ": " + message)
		{
		}
	}

	/**
     * These properties are mostly of interest to the server, which cares
     * about all the intermediate stages since it has to give additional
     * information to the sender.
     */
	public struct ResolverResult
	{
		public IEnumerable<DNSRecord> answers;
		public IEnumerable<DNSRecord> aliases;
		public IEnumerable<DNSRecord> referrals;
		public IEnumerable<DNSRecord> referral_additional;

		public override bool Equals(object other)
		{
			if (!(other is ResolverResult))
			{
				return false;
			}

			var other_result = (ResolverResult)other;
			return answers.SequenceEqual(other_result.answers) &&
						  aliases.SequenceEqual(other_result.aliases) &&
						  referrals.SequenceEqual(other_result.referrals) &&
						  referral_additional.SequenceEqual(other_result.referral_additional);
		}

		public override string ToString()
		{
			return ("ReferralResult[" +
					"Answers=" + String.Join(",", answers) + "\n" +
					"Aliases=" + String.Join(",", aliases) + "\n" +
					"Referrals=" + String.Join(",", referrals) + "\n" +
					"ReferralGlue=" + String.Join(",", referral_additional) + "]");
		}
	}

	public static class ResolverUtils
	{
		private static Logger logger = LogManager.GetCurrentClassLogger();

		/**
         * Sends a DNS query to the given server, and waits for a response.
         * Possibly times out with a SocketException if the response takes
         * too long.
         */
		public static DNSPacket SendQuery(EndPoint server, DNSQuestion question, bool recursive)
		{
			var rng = new Random();
			var packet_id = (UInt16)rng.Next(0, (1 << 16) - 1);

			var to_send = new DNSPacket(
				packet_id, true, QueryType.STANDARD_QUERY,
				false, false, recursive, false, ResponseType.NO_ERROR,
				new DNSQuestion[] { question }, new DNSRecord[0], new DNSRecord[0], new DNSRecord[0]);

			var send_bytes = to_send.ToBytes();

			using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
			{
				socket.ReceiveTimeout = 5 * 1000;
				socket.Bind(new IPEndPoint(IPAddress.Any, 0));

				logger.Trace("Sending packet {0} to {1}", packet_id, server);
				socket.SendTo(send_bytes, server);

				DNSPacket recv_packet = null;
				while (recv_packet == null)
				{

					logger.Trace("Preparing to receive");
					var recv_bytes = new byte[512];
					EndPoint remote_endpoint = new IPEndPoint(IPAddress.Any, 0);
					socket.ReceiveFrom(recv_bytes, ref remote_endpoint);

					recv_packet = DNSPacket.FromBytes(recv_bytes);
					if (recv_packet.Id != packet_id)
					{
						logger.Trace("Trashing bad packet");
						recv_packet = null;
					}
				}

				logger.Trace("Got response {0}", recv_packet);
				return recv_packet;
			}
		}

	}

	/**
     * A basic interface for making DNS requests.
     */
	public interface IResolver
	{
		ResolverResult Resolve(Domain domain, ResourceRecordType rtype, AddressClass addr_class, EndPoint[] servers);
	}

	/**
     * Responsible for interacting with DNS servers to resolve addresses
     * recursively.
     */
	public class StubResolver : IResolver
	{
		private static Logger logger = LogManager.GetCurrentClassLogger();
		private IDNSCache cache;
		private Func<EndPoint, DNSQuestion, bool, DNSPacket> send_query;

		public StubResolver(IDNSCache cache, Func<EndPoint, DNSQuestion, bool, DNSPacket> send_query)
		{
			this.cache = cache;
			this.send_query = send_query;
		}

		/**
         * Tries to resolve a question against the cache, and possibly several
         * servers. Throws a ResolverException if both methods fail.
         */
		public DNSPacket QueryServers(Domain domain, ResourceRecordType record_kind, AddressClass addr_class, EndPoint[] servers)
		{
			var question = new DNSQuestion(domain, record_kind, addr_class);
			foreach (var server in servers)
			{
				try
				{
					var response = send_query(server, question, true);
					if (response.ResponseType == ResponseType.NO_ERROR)
					{
						logger.Trace("Accepting response {0} from {1}", response, server);
						return response;
					}
				}
				catch (SocketException error)
				{
					logger.Trace("Recoverable error: " + error);
				}
			}

			throw new ResolverException(question.Name, "Cannot resolve query " + question);
		}

		/**
         * Does the actual work of resolution, by talking to all the servers and
         * interpreting their answers.
         */
		public ResolverResult Resolve(Domain domain, ResourceRecordType rtype, AddressClass addr_class, EndPoint[] servers)
		{
			var records = cache.Query(domain, addr_class, rtype);
			if (records.Count == 0)
			{
				var packet = QueryServers(domain, rtype, addr_class, servers);
				int found = 0;

				foreach (var resource in packet.Answers)
				{
					if (resource.Resource != null)
					{
						records.Add(resource);
						cache.Add(resource);
						found++;
					}
				}
				logger.Trace("Found {0} answers", found);

				found = 0;
				foreach (var resource in packet.AdditionalRecords)
				{
					if (resource.Resource != null)
					{
						records.Add(resource);
						cache.Add(resource);
					}

					found++;
				}
				logger.Trace("Found {0} additional records", found);

				found = 0;
				foreach (var resource in packet.AuthoritativeAnswers)
				{
					if (resource.Resource != null)
					{
						records.Add(resource);
						cache.Add(resource);
					}

					found++;
				}
				logger.Trace("Found {0} authoritative record", found);
			}
			else
			{
				logger.Trace("Cache returned {0} resources", records.Count);
			}


			var any_results = false;
			var results = new List<DNSRecord>();
			var aliases = new HashSet<Domain>();
			var nameservers = new HashSet<Domain>();
			var nameserver_addrs = new HashSet<IPAddress>();

			var alias_rrs = new List<DNSRecord>();
			var nameserver_rrs = new List<DNSRecord>();
			var nameserver_addr_rrs = new List<DNSRecord>();

			// We'll attack this in stages - first, record all the CNAMEs for the current domain
			// (But only if we're looking specifically for A records - other records
			// generally can't have aliases). Here, we're hoping that the server gives us the addresses
			// in what is basically a topologically sorted order (think of chained CNAMEs as a graph).
			if (rtype == ResourceRecordType.HOST_ADDRESS)
			{
				foreach (var record in records)
				{
					var is_relevant = record.Name == domain || aliases.Contains(record.Name);

					if (record.Resource != null &&
						record.Resource.Type == ResourceRecordType.CANONICAL_NAME &&
						is_relevant)
					{
						logger.Trace("Recording alias {0}", record);
						aliases.Add(((CNAMEResource)record.Resource).Alias);
						alias_rrs.Add(record);

						any_results = true;
					}
				}
			}

			// Next, fish out any addresses that answer the query
			foreach (var record in records)
			{
				var is_relevant = record.Name == domain || aliases.Contains(record.Name);

				if (record.Resource != null && record.Resource.Type == rtype && is_relevant)
				{
					logger.Trace("Recording relevant answer {0}", record);
					results.Add(record);

					any_results = true;
				}
			}

			// Finally, get any referrals or authority claims that we're given
			foreach (var record in records)
			{
				if (record.Resource != null && record.Resource.Type == ResourceRecordType.NAME_SERVER)
				{
					logger.Trace("Recording nameserver {0}", record);
					nameservers.Add(((NSResource)record.Resource).Nameserver);
					nameserver_rrs.Add(record);

					any_results = true;
				}
			}

			// If the server was helpful enough to give us some glue, then make sure
			// to record them as well
			foreach (var record in records)
			{
				if (record.Resource != null && record.Resource.Type == ResourceRecordType.HOST_ADDRESS && nameservers.Contains(record.Name))
				{
					logger.Trace("Recording nameserver glue {0}", record);
					nameserver_addrs.Add(((AResource)record.Resource).Address);
					nameserver_addr_rrs.Add(record);

					any_results = true;
				}
			}

			if (!any_results)
			{
				throw new ResolverException(domain, "Did not get any records relevant to " + domain);
			}

			var result = new ResolverResult();
			result.answers = results;
			result.aliases = alias_rrs;
			result.referrals = nameserver_rrs;
			result.referral_additional = nameserver_addr_rrs;
			return result;
		}
	}
}
