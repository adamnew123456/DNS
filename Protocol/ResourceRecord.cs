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
            var record = new DNSRecord();
            record.Name = stream.ReadDomain();

            var record_type = (ResourceRecordType)stream.ReadUInt16();
            record_type = record_type.Normalize();

            var class_type = (AddressClass)stream.ReadUInt16();
            record.AddressClass = class_type.Normalize();

            record.TimeToLive = stream.ReadUInt32();

            var record_size = stream.ReadUInt16();
            switch (record_type)
            {
                case ResourceRecordType.HOST_ADDRESS:
                    record.Resource = AResource.Unserialize(stream, record_size);
                    break;
                case ResourceRecordType.NAME_SERVER:
                    record.Resource = NSResource.Unserialize(stream, record_size);
                    break;
                case ResourceRecordType.START_OF_AUTHORITY:
                    record.Resource = SOAResource.Unserialize(stream, record_size);
                    break;
                case ResourceRecordType.CANONICAL_NAME:
                    record.Resource = CNAMEResource.Unserialize(stream, record_size);
                    break;
                case ResourceRecordType.MAIL_EXCHANGE:
                    record.Resource = MXResource.Unserialize(stream, record_size);
                    break;
                case ResourceRecordType.UNSUPPORTED:
                    // Advance the byte counter - even if we can't parse it,
                    // we have to properly ignore it
                    stream.ReadBytes(record_size);
                    record.Resource = null;
                    break;
            }

            return record;
        }
    }

    public interface IDNSResource : IEquatable<IDNSResource>
    {
        ResourceRecordType Type { get; }
        void Serialize(DNSOutputStream stream);
    }

    public class AResource : IDNSResource
    {
        public ResourceRecordType Type
        {
            get { return ResourceRecordType.HOST_ADDRESS; }
        }

        // The IP address of the host
        public IPAddress Address;

        public override bool Equals(object other)
        {
            if (!(other is IDNSResource))
            {
                return false;
            }

            return Equals((IDNSResource)other);
        }

        public override int GetHashCode()
        {
            // See SO:
            // http://stackoverflow.com/questions/263400/what-is-the-best-algorithm-for-an-overridden-system-object-gethashcode
            unchecked {
                int hash = 17;
                hash = hash * 29 + (int)Type;
                hash = hash * 29 + Address.GetHashCode();
                return hash;
            }
        }

        public bool Equals(IDNSResource other)
        {
            if (!(other is AResource))
            {
                return false;
            }

            var other_resource = (AResource)other;
            return this.Address.Equals(other_resource.Address);
        }

        public override string ToString()
        {
            return "A[" + Address + "]";
        }

        public void Serialize(DNSOutputStream stream)
        {
            byte[] address_bytes = Address.GetAddressBytes();
            if (address_bytes.Length != 4)
            {
                throw new InvalidDataException("A records cannot have IPv6 addresses");
            }

            stream.WriteBytes(address_bytes);
        }

        public static AResource Unserialize(DNSInputStream stream, UInt16 size)
        {
            var resource = new AResource();
            resource.Address = new IPAddress(stream.ReadBytes(4));
            return resource;
        }
    }

    public class NSResource : IDNSResource
    {
        public ResourceRecordType Type
        {
            get { return ResourceRecordType.NAME_SERVER; }
        }

        // The nameserver that the NS record points to
        public Domain Nameserver;

        public override bool Equals(object other)
        {
            if (!(other is IDNSResource))
            {
                return false;
            }

            return Equals((IDNSResource)other);
        }

        public override int GetHashCode()
        {
            // See SO:
            // http://stackoverflow.com/questions/263400/what-is-the-best-algorithm-for-an-overridden-system-object-gethashcode
            unchecked {
                int hash = 17;
                hash = hash * 29 + (int)Type;
                hash = hash * 29 + Nameserver.GetHashCode();
                return hash;
            }
        }

        public bool Equals(IDNSResource other)
        {
            if (!(other is NSResource))
            {
                return false;
            }

            var other_resource = (NSResource)other;
            return this.Nameserver == other_resource.Nameserver;
        }

        public override string ToString()
        {
            return "NS[" + Nameserver + "]";
        }

        public void Serialize(DNSOutputStream stream)
        {
            stream.WriteDomain(Nameserver);
        }

        public static NSResource Unserialize(DNSInputStream stream, UInt16 size)
        {
            var resource = new NSResource();
            resource.Nameserver = stream.ReadDomain();
            return resource;
        }
    }

    public class SOAResource : IDNSResource
    {
        public ResourceRecordType Type
        {
            get { return ResourceRecordType.START_OF_AUTHORITY; }
        }

        // The hostname of the zone's primary DNS server
        public Domain PrimaryNameServer;

        // The email address of the person responsible for the zone
        // (foo@email.com would be represented as foo.email.com)
        public Domain Hostmaster;

        // A 'version number' which is used to check for changes within this
        // zone. Greater numbers are considered later versions.
        public UInt32 Serial;

        // How many seconds secondary nameservers can cache the information in
        // this record
        public UInt32 RefreshSeconds;

        // How many seconds secondary nameservers should wait before retrying
        // after a failed refresh
        public UInt32 RetrySeconds;

        // How many seconds the secondary nameserver can keep a copy of the
        // zone
        public UInt32 ExpireSeconds;

        // The minimum TTL for everything in this zone
        public UInt32 MinimumTTL;

        public override bool Equals(object other)
        {
            if (!(other is IDNSResource))
            {
                return false;
            }

            return Equals((IDNSResource)other);
        }

        public override int GetHashCode()
        {
            // See SO:
            // http://stackoverflow.com/questions/263400/what-is-the-best-algorithm-for-an-overridden-system-object-gethashcode
            unchecked {
                int hash = 17;
                hash = hash * 29 + (int)Type;
                hash = hash * 29 + PrimaryNameServer.GetHashCode();
                hash = hash * 29 + Hostmaster.GetHashCode();
                hash = hash * 29 + (int)Serial;
                hash = hash * 29 + (int)RefreshSeconds;
                hash = hash * 29 + (int)RetrySeconds;
                hash = hash * 29 + (int)ExpireSeconds;
                hash = hash * 29 + (int)MinimumTTL;
                return hash;
            }
        }

        public bool Equals(IDNSResource other)
        {
            if (!(other is SOAResource))
            {
                return false;
            }

            var other_resource = (SOAResource)other;
            return this.PrimaryNameServer == other_resource.PrimaryNameServer &&
                this.Hostmaster == other_resource.Hostmaster &&
            this.Serial == other_resource.Serial &&
            this.RefreshSeconds == other_resource.RefreshSeconds &&
            this.RetrySeconds == other_resource.RetrySeconds &&
            this.ExpireSeconds == other_resource.ExpireSeconds &&
            this.MinimumTTL == other_resource.MinimumTTL;
        }

        public override string ToString()
        {
            return "SOA[" +
                "PrimaryNS=" + PrimaryNameServer + ", " +
            "Hostmaster=" + Hostmaster + ", " +
            "Serial=" + Serial + ", " +
            "Refresh=" + RefreshSeconds + ", " +
            "Retry=" + RetrySeconds + ", " +
            "Expire=" + ExpireSeconds + ", " +
            "MinTTL=" + MinimumTTL +
            "]";
        }

        public void Serialize(DNSOutputStream stream)
        {
            stream.WriteDomain(PrimaryNameServer);
            stream.WriteDomain(Hostmaster);
            stream.WriteUInt32(Serial);
            stream.WriteUInt32(RefreshSeconds);
            stream.WriteUInt32(RetrySeconds);
            stream.WriteUInt32(ExpireSeconds);
            stream.WriteUInt32(MinimumTTL);
        }

        public static SOAResource Unserialize(DNSInputStream stream, UInt16 size)
        {
            var resource = new SOAResource();
            resource.PrimaryNameServer = stream.ReadDomain();
            resource.Hostmaster = stream.ReadDomain();
            resource.Serial = stream.ReadUInt32();
            resource.RefreshSeconds = stream.ReadUInt32();
            resource.RetrySeconds = stream.ReadUInt32();
            resource.ExpireSeconds = stream.ReadUInt32();
            resource.MinimumTTL = stream.ReadUInt32();
            return resource;
        }
    }

    public class CNAMEResource : IDNSResource
    {
        public ResourceRecordType Type
        {
            get { return ResourceRecordType.CANONICAL_NAME; }
        }

        // The name that the CNAME points to
        public Domain Alias;

        public override bool Equals(object other)
        {
            if (!(other is IDNSResource))
            {
                return false;
            }

            return Equals((IDNSResource)other);
        }

        public override int GetHashCode()
        {
            // See SO:
            // http://stackoverflow.com/questions/263400/what-is-the-best-algorithm-for-an-overridden-system-object-gethashcode
            unchecked {
                int hash = 17;
                hash = hash * 29 + (int)Type;
                hash = hash * 29 + Alias.GetHashCode();
                return hash;
            }
        }

        public bool Equals(IDNSResource other)
        {
            if (!(other is CNAMEResource))
            {
                return false;
            }

            var other_resource = (CNAMEResource)other;
            return this.Alias == other_resource.Alias;
        }

        public override string ToString()
        {
            return "CNAME[" + String.Join(".", Alias) + "]";
        }

        public void Serialize(DNSOutputStream stream)
        {
            stream.WriteDomain(Alias);
        }

        public static CNAMEResource Unserialize(DNSInputStream stream, UInt16 size)
        {
            var resource = new CNAMEResource();
            resource.Alias = stream.ReadDomain();
            return resource;
        }
    }

    public class MXResource : IDNSResource
    {
        public ResourceRecordType Type
        {
            get { return ResourceRecordType.MAIL_EXCHANGE; }
        }

        // How this mail exchange compares against others which have the
        // same name; lower is more preferred
        public UInt16 Preference;

        // The hostname of the machine that is acting as the mail exchange
        public Domain Mailserver;

        public override bool Equals(object other)
        {
            if (!(other is IDNSResource))
            {
                return false;
            }

            return Equals((IDNSResource)other);
        }

        public override int GetHashCode()
        {
            // See SO:
            // http://stackoverflow.com/questions/263400/what-is-the-best-algorithm-for-an-overridden-system-object-gethashcode
            unchecked {
                int hash = 17;
                hash = hash * 29 + (int)Type;
                hash = hash * 29 + Preference;
                hash = hash * 29 + Mailserver.GetHashCode();
                return hash;
            }
        }

        public bool Equals(IDNSResource other)
        {
            if (!(other is MXResource))
            {
                return false;
            }

            var other_resource = (MXResource)other;
            return this.Preference == other_resource.Preference &&
                this.Mailserver == other_resource.Mailserver;
        }

        public override string ToString()
        {
            return "MX[" +
            "Preference=" + Preference + ", " +
            "Mailserver=" + String.Join(".", Mailserver) +
            "]";
        }

        public void Serialize(DNSOutputStream stream)
        {
            stream.WriteUInt16(Preference);
            stream.WriteDomain(Mailserver);
        }

        public static MXResource Unserialize(DNSInputStream stream, UInt16 size)
        {
            var resource = new MXResource();
            resource.Preference = stream.ReadUInt16();
            resource.Mailserver = stream.ReadDomain();
            return resource;
        }
    }
}

