using System;
using System.IO;
using System.Net;

namespace DNSProtocol
{
    /**
     * The base of all of the different DNS record types supported
     * by this server.
     */
    public class DNSRecord: IEquatable<DNSRecord>
    {
        // The domain that this record has information on.
        public Domain Name;

        // The address class of the data in the record
        public AddressClass AddressClass;

        // How long the record can be cached in seconds
        public UInt32 TimeToLive;

        // The resource record that this record stores
        public IDNSResource Resource;

		public DNSRecord(Domain name, AddressClass address_class, UInt32 ttl, IDNSResource resource)
		{
			Name = name;
			AddressClass = address_class;
			TimeToLive = ttl;
			Resource = resource;
		}

        public override bool Equals(object other)
        {
            if (!(other is DNSRecord))
            {
                return false;
            }

            return Equals((DNSRecord)other);
        }

        public override int GetHashCode()
        {
            // See SO:
            // http://stackoverflow.com/questions/263400/what-is-the-best-algorithm-for-an-overridden-system-object-gethashcode
            unchecked {
                int hash = 17;
                hash = hash * 29 + Name.GetHashCode();
                hash = hash * 29 + (int)AddressClass;
                hash = hash * 29 + (int)TimeToLive;
                if (Resource != null)
                {
                    hash = hash * 29 + Resource.GetHashCode();
                }
                return hash;
            }
        }

        public bool Equals(DNSRecord other)
        {
            return this.Name == other.Name &&
                this.AddressClass == other.AddressClass &&
                this.TimeToLive == other.TimeToLive &&
                this.Resource.Equals(other.Resource);
        }

        public override string ToString()
        {
            return "ResourceRecord(" +
            "Domain=" + Name + ", " +
            "Class=" + AddressClass + ", " +
            "TTL=" + TimeToLive + ", " +
            Resource +
            " )";
        }

        /**
         * Converts a record object into the raw bytes that represent
         * it on the wire.
         */
        public void Serialize(DNSOutputStream stream)
        {
            stream.WriteDomain(Name);
            stream.WriteUInt16((UInt16)Resource.Type);
            stream.WriteUInt16((UInt16)AddressClass);
            stream.WriteUInt32(TimeToLive);

            var length_hole = stream.WriteUInt16Hole();
            var start_posn = stream.Position;
            Resource.Serialize(stream);
            var end_posn = stream.Position;
            length_hole.Fill((UInt16)(end_posn - start_posn));
        }

        /**
         * Decodes the raw bytes of a DNS resource record into an
         * object.
         */
        public static DNSRecord Unserialize(DNSInputStream stream)
        {
            var name = stream.ReadDomain();
            var record_type = (ResourceRecordType)stream.ReadUInt16();
            var class_type = (AddressClass)stream.ReadUInt16();
            var ttl = stream.ReadUInt32();

            var record_size = stream.ReadUInt16();
			IDNSResource resource = null;
			switch (record_type.Normalize())
            {
                case ResourceRecordType.HOST_ADDRESS:
                    resource = AResource.Unserialize(stream, record_size);
                    break;
                case ResourceRecordType.NAME_SERVER:
                    resource = NSResource.Unserialize(stream, record_size);
                    break;
                case ResourceRecordType.START_OF_AUTHORITY:
                    resource = SOAResource.Unserialize(stream, record_size);
                    break;
                case ResourceRecordType.CANONICAL_NAME:
                    resource = CNAMEResource.Unserialize(stream, record_size);
                    break;
                case ResourceRecordType.MAIL_EXCHANGE:
                    resource = MXResource.Unserialize(stream, record_size);
                    break;
				case ResourceRecordType.POINTER:
					resource = PTRResource.Unserialize(stream, record_size);
					break;
                case ResourceRecordType.UNSUPPORTED:
                    // Advance the byte counter - even if we can't parse it,
                    // we have to properly ignore it
                    stream.ReadBytes(record_size);
                    resource = null;
                    break;
            }

			return new DNSRecord(name, class_type.Normalize(), ttl, resource);
        }
    }

    public interface IDNSResource : IEquatable<IDNSResource>
    {
        ResourceRecordType Type { get; }
        void Serialize(DNSOutputStream stream);
    }
}

