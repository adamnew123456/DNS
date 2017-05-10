using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;

using NLog;

using DNSProtocol;
using DNSResolver;

namespace DNSServer
{
    /**
     * The query executor is responsible for fielding DNS queries and producing
     * appropriate responses. It knows both about the local zone for things that
     * we are authoritative over, and it can also punt to a resolver in cases
     * where the requester wants recursive service but we aren't authoritative.
     */
    class QueryExecutor
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private DNSZone zone;
        private IDNSCache cache;
        private IResolver resolver;

        /**
		     * We have to differentiate main answers, authority answers, and additional 
		     * answers without necessarily constructing a whole packet.
		     */
        public struct QueryResult
        {
            public bool IsAuthority;
            public bool FoundAnswer;
            public List<DNSRecord> Answers, Authority, Additional;
        }

        public QueryExecutor(DNSZone zone, IResolver resolver, IDNSCache cache)
        {
            this.zone = zone;
            this.resolver = resolver;
            this.cache = cache;
        }

        /**
         * Resolves the given query, returning a QueryResult that contains
         * everything we found while doing the resolution.
         */
        public QueryResult Execute(Domain domain, ResourceRecordType rtype, AddressClass addr_class, bool recursive)
        {
            logger.Trace("Executing query on {0} for type {1} with recursion {2}",
                         domain, rtype, recursive);

            if (zone.IsAuthorityFor(domain))
            {
                var records = zone.Query(domain, rtype, addr_class).ToList();

                // It is possible that there is a CNAME that we should be aware of - in that case, check
                // it and see if we can find one.
                if (records.Count == 0)
                {
                    var cname_records = zone.Query(domain, ResourceRecordType.CANONICAL_NAME, addr_class).ToArray();

                    if (cname_records.Length != 0)
                    {
                        logger.Trace("Authoritative for CNAMEs, re-executing");

                        // In this case, try again with the alias
                        var alias = ((CNAMEResource)cname_records[0].Resource).Alias;
                        var alias_results = Execute(alias, rtype, addr_class, recursive);

                        // The RFC directs us to return any intermediate CNAMEs, which we do
                        alias_results.Answers.InsertRange(0, cname_records);

                        return alias_results;
                    }
                }

                var result = new QueryResult();
                result.IsAuthority = true;
                result.FoundAnswer = true;
                result.Answers = new List<DNSRecord>();
                result.Authority = new List<DNSRecord>();
                result.Additional = new List<DNSRecord>();

                result.Answers.AddRange(records);
                result.Authority.Add(zone.StartOfAuthority);
                return result;
            }
            else
            {
                var owning_subzone = zone.FindSubZone(domain);
                if (owning_subzone != null)
                {
                    logger.Trace("Subzone {0} is authoritative", owning_subzone);

                    // We can punt on the computation to our subzone delgation
					var subzone_nameservers = zone.Query(owning_subzone, ResourceRecordType.NAME_SERVER, addr_class);
                    var subzone_nameserver_addr_records = subzone_nameservers
						.SelectMany(ns => zone.Query(ns.Name, ResourceRecordType.HOST_ADDRESS, addr_class));
                    var subzone_nameserver_addrs = subzone_nameserver_addr_records.Select(
                                                       record => new IPEndPoint(((AResource)record.Resource).Address, 53)
                                                   ).ToArray();

                    IEnumerable<DNSRecord> response = null;
                    try
                    {
                        var info = resolver.Resolve(domain, ResourceRecordType.HOST_ADDRESS, addr_class, subzone_nameserver_addrs);
                        response = info.aliases.Concat(info.answers).ToList();
                    }
                    catch (ResolverException err)
                    {
                        logger.Trace("Could not resolve from subzone: {0}", err);
                        response = new DNSRecord[] { };
                    }

                    var result = new QueryResult();
                    result.IsAuthority = false;
                    result.FoundAnswer = response.Count() > 0;
                    result.Answers = new List<DNSRecord>(response);
                    result.Authority = new List<DNSRecord>(subzone_nameservers);
                    result.Additional = new List<DNSRecord>(subzone_nameserver_addr_records);
                    return result;
                }
                else if (recursive)
                {
                    // We'll have to go outside our zone and use the general-purpose resolver
                    ResolverResult response;
                    logger.Trace("No authoritative server is local, executing recursive resolver");

                    try
                    {
                        response = resolver.Resolve(domain, rtype, addr_class, zone.Relays);
                    }
                    catch (ResolverException err)
                    {
                        logger.Trace("Could not resolve: {0}", err);

                        response = new ResolverResult();
                        response.answers = new List<DNSRecord>();
                        response.aliases = new List<DNSRecord>();
                        response.referrals = new List<DNSRecord>();
                        response.referral_additional = new List<DNSRecord>();
                    }

                    var result = new QueryResult();
                    result.IsAuthority = false;
                    result.FoundAnswer = response.answers.Count() > 0;
                    result.Answers = response.aliases.Concat(response.answers).ToList();
                    result.Authority = response.referrals.ToList();
                    result.Additional = response.referral_additional.ToList();
                    return result;
                }
                else
                {
                    var cached_responses = cache.Query(domain, AddressClass.INTERNET, rtype);
                    if (cached_responses.Count > 0)
                    {
                        logger.Trace("Non-recursive search found {0} cached results", cached_responses.Count);

                        var cached_result = new QueryResult();
                        cached_result.IsAuthority = false;
                        cached_result.FoundAnswer = true;
                        cached_result.Answers = cached_responses.ToList();
                        cached_result.Additional = new List<DNSRecord>();
                        cached_result.Authority = new List<DNSRecord>();
                        return cached_result;
                    }

                    // If we can't recurse, and our cache knows nothing, then punt onto the forwarder
                    logger.Trace("Executing limited-case non-recursive resolver");

					var question = new DNSQuestion(domain, rtype, AddressClass.INTERNET);

                    foreach (var forwarder in zone.Relays)
                    {
                        try
                        {
                            var forward_result = ResolverUtils.SendQuery(forwarder, question, false);

                            // If the server doesn't like our request, then pass it to something else
                            if (forward_result.ResponseType != ResponseType.NO_ERROR &&
                                forward_result.ResponseType != ResponseType.NAME_ERROR)
                            {
                                continue;
                            }

                            var forward_return = new QueryResult();
                            forward_return.FoundAnswer = forward_result.ResponseType == ResponseType.NO_ERROR;
                            forward_return.IsAuthority = false;
                            forward_return.Answers = forward_result.Answers.ToList();
                            forward_return.Additional = forward_result.AdditionalRecords.ToList();
                            forward_return.Authority = forward_result.AuthoritativeAnswers.ToList();
                            return forward_return;
                        }
                        catch (SocketException err)
                        {
                            // We can safely punt onto the next forwarder if one bails
                            logger.Trace("Could not request from {0}: {1}", forwarder, err);
                        }
                    }

                    // We can't do anything else here, so there is no such host, as far as we knot
                    var result = new QueryResult();
                    result.FoundAnswer = false;
                    result.IsAuthority = false;
                    result.Answers = new List<DNSRecord>();
                    result.Authority = new List<DNSRecord>();
                    result.Additional = new List<DNSRecord>();
                    return result;
                }
            }
        }
    }

    /**
     * The UDP server is the networked portion of the server, which accepts
     * queries from the network and then sends responses back.
     */
    class UDPServer
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private EndPoint bind_address;
        private QueryExecutor query_exec;
        private Socket server;

        public UDPServer(EndPoint bind, QueryExecutor qexec)
        {
            bind_address = bind;
            query_exec = qexec;
        }

        public void Start()
        {
            server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            server.EnableBroadcast = true;
            server.Bind(bind_address);

            while (true)
            {
                var buffer = new byte[512];
                var endpoint = new IPEndPoint(IPAddress.Any, 0);

                var endpoint_cast = (EndPoint)endpoint;
                server.ReceiveFrom(buffer, ref endpoint_cast);

                endpoint = (IPEndPoint)endpoint_cast;
                logger.Trace("Got request from {0}", endpoint);
                Task.Run(() => RespondToClient(buffer, endpoint));
            }
        }

        private void RespondToClient(byte[] buffer, EndPoint peer)
        {
			UInt16 outbound_id;
			var outbound_is_authority = false;
			QueryType outbound_qtype;
			bool outbound_recursive_request;
			bool outbound_recursive_response = false;
			ResponseType outbound_rtype;
			var outbound_questions = new List<DNSQuestion>();
			var outbound_answers = new List<DNSRecord>();
			var outbound_authoritative = new List<DNSRecord>();
			var outbound_additional = new List<DNSRecord>();

            try
            {
                var inbound = DNSPacket.FromBytes(buffer);
				outbound_id = inbound.Id;
				outbound_qtype = inbound.QueryType;
				outbound_recursive_request = inbound.RecursiveRequest;

                logger.Trace("{0}: Parsed inbound request {1}", peer, inbound);
				outbound_questions.AddRange(inbound.Questions);

                if (inbound.QueryType == QueryType.UNSUPPORTED)
                {
                    logger.Trace("{0}: Unsupported query type", peer);
					outbound_rtype = ResponseType.NOT_IMPLEMENTED;
                }
                else if (inbound.Questions.Length != 1)
                {
                    // BIND does this, so apparently we're in good company if we turn down multi-question packets
                    logger.Trace("{0}: Multi-question packet", peer);
					outbound_rtype = ResponseType.NOT_IMPLEMENTED;
                }
                else
                {
					outbound_recursive_response = outbound_recursive_request;

                    var question = inbound.Questions[0];
                    // We *could* find a way to communicate the rejection of a
                    // single question, but it's simpler to just reject the
                    // whole thing
                    if (question.QueryType == ResourceRecordType.UNSUPPORTED ||
                        question.AddressClass == AddressClass.UNSUPPORTED)
                    {
                        logger.Trace("{0}: Unsupported question {1}", peer, question);
						outbound_rtype = ResponseType.NOT_IMPLEMENTED;
                    }
                    else
                    {
                        try
                        {
							var answer = query_exec.Execute(question.Name, question.QueryType, question.AddressClass, inbound.RecursiveRequest);

                            if (answer.FoundAnswer)
                            {
                                logger.Trace("{0}: Resolver found answer for {1}", peer, question);
								outbound_rtype = ResponseType.NO_ERROR;
								outbound_is_authority = answer.IsAuthority;
								outbound_answers.AddRange(answer.Answers);
								outbound_additional.AddRange(answer.Additional);
								outbound_authoritative.AddRange(answer.Authority);
                            }
                            else
                            {
                                logger.Trace("{0}: Could not find the name {1}", peer, question.Name);
								outbound_rtype = ResponseType.NAME_ERROR;
								outbound_is_authority = answer.IsAuthority;
                            }
                        }
                        catch (Exception err)
                        {
                            Console.WriteLine(err);

                            logger.Error(err, "{0}: Server failure", peer);
							outbound_rtype = ResponseType.SERVER_FAILURE;
							outbound_is_authority = false;
                        }

                    }
                }
            }
            catch (InvalidDataException err)
            {
                // If this is the case, we have to at least extract the ID so
                // that the peer knows what we're complaining about
                logger.Trace("{0}: Unparsable request ({1})", peer, err.Message);

                var in_id = (UInt16)((buffer[0] << 8) + buffer[1]);
				outbound_id = in_id;
				outbound_qtype = QueryType.STANDARD_QUERY;
				outbound_is_authority = false;
				outbound_recursive_request = false;
				outbound_recursive_response = false;
				outbound_rtype = ResponseType.FORMAT_ERROR;
            }

			var outbound = new DNSPacket(
				outbound_id,
				false,
				outbound_qtype,
				outbound_is_authority,
				false,
				outbound_recursive_request,
				outbound_recursive_response,
				outbound_rtype,
				outbound_questions,
				outbound_answers,
				outbound_authoritative,
				outbound_additional);

            var outbound_bytes = outbound.ToBytes();
            logger.Trace("{0}: Sending {1}-byte response {2}", peer, outbound_bytes.Length, outbound);
            server.SendTo(outbound_bytes, peer);
        }
    }

    public class MainClass
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public static void Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.WriteLine("Usage: dns-server <config-file> <ip-address> <port>");
                Environment.Exit(1);
            }

            var config = new XmlDocument();
            config.Load(args[0]);

            var zone = DNSZone.Unserialize(config);
            var clock = new StopwatchClock();
            var cache = new ResolverCache(clock, 2048);
			var resolver = new StubResolver(cache, ResolverUtils.SendQuery);
            var query_exec = new QueryExecutor(zone, resolver, cache);

            var bind_addr = new IPEndPoint(IPAddress.Parse(args[1]),
                                int.Parse(args[2]));

            var server = new UDPServer(bind_addr, query_exec);
            server.Start();
        }
    }
}
