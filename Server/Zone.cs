using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml;

using NLog;

using DNSProtocol;

namespace DNSServer
{
    /**
     * The zone represents all the records that we have authoritative
     * knowledge over. When a query comes in that we're an authority for, we
     * need to find out what records are relevant.
     */
    class DNSZone
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

		private Dictionary<Tuple<Domain, ResourceRecordType, AddressClass>, HashSet<DNSRecord>> zone_records;

		public IEnumerable<DNSRecord> Records
		{
			get
			{
				return zone_records.Values.SelectMany(_ => _);
			}
		}

        // We want to make sure that we can efficiently check and see if a domain is a part of our zone
        public DNSRecord StartOfAuthority;
        private HashSet<Domain> sub_zones;
        public EndPoint[] Relays;

        public DNSZone(DNSRecord start_of_authority, EndPoint[] relays)
        {
			zone_records = new Dictionary<Tuple<Domain, ResourceRecordType, AddressClass>, HashSet<DNSRecord>>();
            StartOfAuthority = start_of_authority;
            Relays = relays;
            sub_zones = new HashSet<Domain>();
        }

        /**
         * Registers a new record with the zone.
         */
        public void Add(DNSRecord record)
        {
			var key = Tuple.Create(record.Name, record.Resource.Type, record.AddressClass);
            if (!zone_records.ContainsKey(key))
            {
                zone_records[key] = new HashSet<DNSRecord>();
            }

            zone_records[key].Add(record);

            /*
			       * This indicates a delegation to another nameserver for a subzone. For example, if our SOA
			       * is zen.com but we have an NS record for content.zen.com, then we're not responsible for
			       * content.zen.com and we should resolve it like anything else.
			       */
            if (record.Resource.Type == ResourceRecordType.NAME_SERVER &&
                StartOfAuthority.Name.IsSubdomain(record.Name) &&
                StartOfAuthority.Name != record.Name)
            {
                sub_zones.Add(record.Name);
            }
        }

        /**
         * Gets all records with the given domain and record type in the zone.
         */
		public IEnumerable<DNSRecord> Query(Domain domain, ResourceRecordType rtype, AddressClass addr_class)
        {
            var key = Tuple.Create(domain, rtype, addr_class);
            HashSet<DNSRecord> records;
            zone_records.TryGetValue(key, out records);

            if (records == null)
            {
                return new HashSet<DNSRecord>();
            }
            else
            {
                return records;
            }
        }

        /**
		     * Checks to see if a domain is within the authority of this zone.
		     */
        public bool IsAuthorityFor(Domain domain)
        {
            if (!StartOfAuthority.Name.IsSubdomain(domain))
            {
                return false;
            }

            foreach (var zone in sub_zones)
            {
				// We should be an authority for foo.example.com itself, even if foo.example.com
				// is the name of a subzone, since we might have a CNAME or something for the
				// subzone itself
                if (zone != domain && zone.IsSubdomain(domain))
                {
                    return false;
                }
            }

            return true;
        }

        /**
		     * Figure out what sub-zone a domain is part of - returns null if there is no such sub-zone.
		     */
        public Domain FindSubZone(Domain domain)
        {
            if (!StartOfAuthority.Name.IsSubdomain(domain))
            {
                return null;
            }

            foreach (var zone in sub_zones)
            {
                if (zone.IsSubdomain(domain))
                {
                    return zone;
                }
            }

            return null;
        }

        /**
         * Produces a new zone from an input XML file.
         */
        public static DNSZone Unserialize(XmlDocument config)
        {
            DNSRecord start_of_authority = null;
            var records = new List<DNSRecord>();
            var relays = new List<EndPoint>();

            if (config.DocumentElement.Name != "zone")
            {
                throw new InvalidDataException("Root element must be called zone");
            }

            foreach (var entry in config.DocumentElement.ChildNodes.OfType<XmlNode>())
            {

                if (entry.NodeType != XmlNodeType.Element)
                {
                    logger.Trace("Ignoring node of type {0}", entry.NodeType);
                    continue;
                }

                bool is_record =
                    entry.Name == "A" ||
                    entry.Name == "NS" ||
                    entry.Name == "CNAME" ||
                    entry.Name == "MX" ||
                    entry.Name == "SOA";

                if (is_record)
                {
                    if (entry.Attributes["name"] == null ||
                        entry.Attributes["class"] == null ||
                        entry.Attributes["ttl"] == null)
                    {
                        throw new InvalidDataException("Resource records must have 'name', 'class' and 'ttl' attributes");
                    }

                    var record_name = new Domain(entry.Attributes["name"].Value);

					AddressClass record_class;
					switch (entry.Attributes["class"].Value)
					{
						case "IN":
							record_class = AddressClass.INTERNET;
							break;
						default:
	                        throw new InvalidDataException("Only address class 'IN' is supported");
                    }

					UInt32 record_ttl = 0;
                    try
                    {
                        record_ttl = UInt16.Parse(entry.Attributes["ttl"].Value);
                    }
                    catch (InvalidDataException err)
                    {
                        throw new InvalidDataException(entry.Attributes["ttl"].Value + " is not a valid TTL");
                    }

                    IDNSResource resource = null;
                    switch (entry.Name)
                    {
                        case "A":
                            if (entry.Attributes["address"] == null)
                            {
                                throw new InvalidDataException("A record must have address");
                            }

                            try
                            {
								resource = new AResource(IPAddress.Parse(entry.Attributes["address"].Value));
                            }
                            catch (FormatException err)
                            {
                                throw new InvalidDataException(entry.Attributes["address"].Value + " is not a valid IPv4 address");
                            }

                            logger.Trace("A record: address={0}", ((AResource)resource).Address);
                            break;

                        case "NS":
                            if (entry.Attributes["nameserver"] == null)
                            {
                                throw new InvalidDataException("NS record must have a nameserver");
                            }

							resource = new NSResource(new Domain(entry.Attributes["nameserver"].Value));

                            logger.Trace("NS record: nameserver={0}", ((NSResource)resource).Nameserver);
                            break;

                        case "CNAME":
                            if (entry.Attributes["alias"] == null)
                            {
                                throw new InvalidDataException("CNAME record must have an alias");
                            }

							resource = new CNAMEResource(new Domain(entry.Attributes["alias"].Value));

                            logger.Trace("CNAME record: alias={0}", ((CNAMEResource)resource).Alias);
                            break;

                        case "MX":
                            if (entry.Attributes["priority"] == null ||
                                entry.Attributes["mailserver"] == null)
                            {
                                throw new InvalidDataException("MX record must have priority and mailserver");
                            }

                            var mailserver = new Domain(entry.Attributes["mailserver"].Value);

							UInt16 preference = 0;
                            try
                            {
								preference = UInt16.Parse(entry.Attributes["priority"].Value);
                            }
                            catch (FormatException err)
                            {
                                throw new InvalidDataException(entry.Attributes["priority"].Value + " is not a valid priority value");
                            }

							resource = new MXResource(preference, mailserver);

                            logger.Trace("MX record: priority={0} mailserver={1}",
                                ((MXResource)resource).Preference,
                                ((MXResource)resource).Mailserver);
                            break;

                        case "SOA":
                            if (entry.Attributes["primary-ns"] == null ||
                                entry.Attributes["hostmaster"] == null ||
                                entry.Attributes["serial"] == null ||
                                entry.Attributes["refresh"] == null ||
                                entry.Attributes["retry"] == null ||
                                entry.Attributes["expire"] == null ||
                                entry.Attributes["min-ttl"] == null)
                            {
                                throw new InvalidDataException("SOA record missing one of: primary-ns, hostmaster, serial, refresh, retry, expire and min-ttl");
                            }

							var primary_ns = new Domain(entry.Attributes["primary-ns"].Value);
							var hostmaster = new Domain(entry.Attributes["hostmaster"].Value);

							UInt32 serial = 0;
                            try
                            {
                                serial = UInt16.Parse(entry.Attributes["serial"].Value);
                            }
                            catch (FormatException err)
                            {
                                throw new InvalidDataException(entry.Attributes["serial"].Value + " is not a valid serial number");
                            }

							UInt32 refresh = 0;
                            try
							{
                                refresh = UInt16.Parse(entry.Attributes["refresh"].Value);
                            }
                            catch (FormatException err)
                            {
                                throw new InvalidDataException(entry.Attributes["refresh"].Value + " is not a valid refresh value");
                            }

							UInt32 retry = 0;
                            try
                            {
                                retry = UInt16.Parse(entry.Attributes["retry"].Value);
                            }
                            catch (FormatException err)
                            {
                                throw new InvalidDataException(entry.Attributes["retry"].Value + " is not a valid retry value");
                            }

							UInt32 expire = 0;
                            try
                            {
                                expire = UInt16.Parse(entry.Attributes["expire"].Value);
                            }
                            catch (FormatException err)
                            {
                                throw new InvalidDataException(entry.Attributes["expire"].Value + " is not a valid expire value");
                            }

							UInt32 minttl = 0;
                            try
                            {
                                minttl = UInt16.Parse(entry.Attributes["min-ttl"].Value);
                            }
                            catch (FormatException err)
                            {
                                throw new InvalidDataException(entry.Attributes["min-ttl"].Value + " is not a valid expire value");
                            }

							resource = new SOAResource(primary_ns, hostmaster, serial, refresh, retry, expire, minttl);

                            logger.Trace("SOA record: primary-ns={0} hostmaster={1} serial={2} refresh={3} retry={4} expire={5} min-ttl={6}",
                                ((SOAResource)resource).PrimaryNameServer,
                                ((SOAResource)resource).Hostmaster,
                                ((SOAResource)resource).Serial,
                                ((SOAResource)resource).RefreshSeconds,
                                ((SOAResource)resource).RetrySeconds,
                                ((SOAResource)resource).ExpireSeconds,
                                ((SOAResource)resource).MinimumTTL);

                            break;
                    }

					var record = new DNSRecord(record_name, record_class, record_ttl, resource);
                    if (record.Resource.Type == ResourceRecordType.START_OF_AUTHORITY)
                    {
                        if (start_of_authority == null)
                        {
                            logger.Trace("Found SOA: {0}", record);
                            start_of_authority = record;
                        }
                        else
                        {
                            throw new InvalidDataException("Cannot have more than one SOA record in zone");
                        }
                    }
                    else
                    {
                        logger.Trace("Found other record: {0}", record);
                        records.Add(record);
                    }
                }
                else if (entry.Name == "relay")
                {
                    if (entry.Attributes["address"] == null ||
                        entry.Attributes["port"] == null)
                    {
                        throw new InvalidDataException("relay record must have address and port");
                    }

                    IPAddress address;
                    int port;

                    try
                    {
                        address = IPAddress.Parse(entry.Attributes["address"].Value);
                    }
                    catch (FormatException err)
                    {
                        throw new InvalidDataException(entry.Attributes["address"].Value + " is not a valid IPv4 address");
                    }

                    try
                    {
                        port = int.Parse(entry.Attributes["port"].Value);
                    }
                    catch (FormatException err)
                    {
                        throw new InvalidDataException(entry.Attributes["port"].Value + " is not a valid port");
                    }

                    relays.Add(new IPEndPoint(address, port));
                    logger.Trace("Found relay: {0}:{1}", address, port);
                }
                else
                {
                    throw new InvalidDataException(entry.Name + " is not a valid zone entry");
                }
            }

            if (start_of_authority == null)
            {
                throw new InvalidDataException("Zone does not have SOA record");
            }

            var zone = new DNSZone(start_of_authority, relays.ToArray());

            foreach (var record in records)
            {
                if (record.TimeToLive < ((SOAResource)start_of_authority.Resource).MinimumTTL)
                {
                    logger.Trace("Correcting TTL: Record {0} has smaller TTL than SOA MinTTL {1}",
                                 record, start_of_authority);
                    record.TimeToLive = ((SOAResource)start_of_authority.Resource).MinimumTTL;
                }

                zone.Add(record);
            }

            return zone;
        }
    }
}
