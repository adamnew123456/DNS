
using System;
using System.Collections.Generic;
using System.Net;

namespace DNSProtocol
{
    public class AResource : IDNSResource 
    {        
	    public ResourceRecordType Type
	    {
		    get { return ResourceRecordType.HOST_ADDRESS; }
	    }

	    public readonly IPv4Address Address;
        public AResource(IPv4Address Address_) {
		    Address = Address_;
		}

		public override bool Equals(object other)
		{

			if (!(other is IDNSResource))
			{
				return false;
			}

			return Equals((IDNSResource)other);
		}

		public bool Equals(IDNSResource other)
		{
			if (!(other is AResource))
			{
				return false;
			}

			var other_resource = (AResource)other;
			return
                 this.Address.Equals(other_resource.Address) 
            ;
		}

		public override int GetHashCode()
		{
			unchecked {
				int hash = 17;
                
                hash = hash * 29 + Address.GetHashCode();
				return hash;
			}
		}

		public override string ToString()
		{
			string buffer = "A[";
            buffer += " Address=" + Address;
			return buffer + "]";
		}

		public void Serialize(DNSOutputStream stream)
		{
          stream.WriteIPv4Address(Address);
		}

		public static AResource Unserialize(DNSInputStream stream, UInt16 size)
		{
			return new AResource(
				stream.ReadIPv4Address()
			);
		}
	}
    public class AAAAResource : IDNSResource 
    {        
	    public ResourceRecordType Type
	    {
		    get { return ResourceRecordType.HOST_6ADDRESS; }
	    }

	    public readonly IPv6Address Address;
        public AAAAResource(IPv6Address Address_) {
		    Address = Address_;
		}

		public override bool Equals(object other)
		{

			if (!(other is IDNSResource))
			{
				return false;
			}

			return Equals((IDNSResource)other);
		}

		public bool Equals(IDNSResource other)
		{
			if (!(other is AAAAResource))
			{
				return false;
			}

			var other_resource = (AAAAResource)other;
			return
                 this.Address.Equals(other_resource.Address) 
            ;
		}

		public override int GetHashCode()
		{
			unchecked {
				int hash = 17;
                
                hash = hash * 29 + Address.GetHashCode();
				return hash;
			}
		}

		public override string ToString()
		{
			string buffer = "AAAA[";
            buffer += " Address=" + Address;
			return buffer + "]";
		}

		public void Serialize(DNSOutputStream stream)
		{
          stream.WriteIPv6Address(Address);
		}

		public static AAAAResource Unserialize(DNSInputStream stream, UInt16 size)
		{
			return new AAAAResource(
				stream.ReadIPv6Address()
			);
		}
	}
    public class NSResource : IDNSResource 
    {        
	    public ResourceRecordType Type
	    {
		    get { return ResourceRecordType.NAME_SERVER; }
	    }

	    public readonly Domain Nameserver;
        public NSResource(Domain Nameserver_) {
		    Nameserver = Nameserver_;
		}

		public override bool Equals(object other)
		{

			if (!(other is IDNSResource))
			{
				return false;
			}

			return Equals((IDNSResource)other);
		}

		public bool Equals(IDNSResource other)
		{
			if (!(other is NSResource))
			{
				return false;
			}

			var other_resource = (NSResource)other;
			return
                 this.Nameserver == other_resource.Nameserver 
            ;
		}

		public override int GetHashCode()
		{
			unchecked {
				int hash = 17;
                
                hash = hash * 29 + Nameserver.GetHashCode();
				return hash;
			}
		}

		public override string ToString()
		{
			string buffer = "NS[";
            buffer += " Nameserver=" + Nameserver;
			return buffer + "]";
		}

		public void Serialize(DNSOutputStream stream)
		{
          stream.WriteDomain(Nameserver);
		}

		public static NSResource Unserialize(DNSInputStream stream, UInt16 size)
		{
			return new NSResource(
				stream.ReadDomain()
			);
		}
	}
    public class SOAResource : IDNSResource 
    {        
	    public ResourceRecordType Type
	    {
		    get { return ResourceRecordType.START_OF_AUTHORITY; }
	    }

	    public readonly Domain PrimaryNameServer;
	    public readonly Domain Hostmaster;
	    public readonly UInt32 Serial;
	    public readonly UInt32 RefreshSeconds;
	    public readonly UInt32 RetrySeconds;
	    public readonly UInt32 ExpireSeconds;
	    public readonly UInt32 MinimumTTL;
        public SOAResource(Domain PrimaryNameServer_,Domain Hostmaster_,UInt32 Serial_,UInt32 RefreshSeconds_,UInt32 RetrySeconds_,UInt32 ExpireSeconds_,UInt32 MinimumTTL_) {
		    PrimaryNameServer = PrimaryNameServer_;
		    Hostmaster = Hostmaster_;
		    Serial = Serial_;
		    RefreshSeconds = RefreshSeconds_;
		    RetrySeconds = RetrySeconds_;
		    ExpireSeconds = ExpireSeconds_;
		    MinimumTTL = MinimumTTL_;
		}

		public override bool Equals(object other)
		{

			if (!(other is IDNSResource))
			{
				return false;
			}

			return Equals((IDNSResource)other);
		}

		public bool Equals(IDNSResource other)
		{
			if (!(other is SOAResource))
			{
				return false;
			}

			var other_resource = (SOAResource)other;
			return
                 this.PrimaryNameServer == other_resource.PrimaryNameServer 
                   &&                  this.Hostmaster == other_resource.Hostmaster 
                   &&                  this.Serial == other_resource.Serial 
                   &&                  this.RefreshSeconds == other_resource.RefreshSeconds 
                   &&                  this.RetrySeconds == other_resource.RetrySeconds 
                   &&                  this.ExpireSeconds == other_resource.ExpireSeconds 
                   &&                  this.MinimumTTL == other_resource.MinimumTTL 
            ;
		}

		public override int GetHashCode()
		{
			unchecked {
				int hash = 17;
                
                hash = hash * 29 + PrimaryNameServer.GetHashCode();                
                hash = hash * 29 + Hostmaster.GetHashCode();                
                hash = hash * 29 + Serial.GetHashCode();                
                hash = hash * 29 + RefreshSeconds.GetHashCode();                
                hash = hash * 29 + RetrySeconds.GetHashCode();                
                hash = hash * 29 + ExpireSeconds.GetHashCode();                
                hash = hash * 29 + MinimumTTL.GetHashCode();
				return hash;
			}
		}

		public override string ToString()
		{
			string buffer = "SOA[";
            buffer += " PrimaryNameServer=" + PrimaryNameServer;
            buffer += " Hostmaster=" + Hostmaster;
            buffer += " Serial=" + Serial;
            buffer += " RefreshSeconds=" + RefreshSeconds;
            buffer += " RetrySeconds=" + RetrySeconds;
            buffer += " ExpireSeconds=" + ExpireSeconds;
            buffer += " MinimumTTL=" + MinimumTTL;
			return buffer + "]";
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
			return new SOAResource(
				stream.ReadDomain()
                ,				stream.ReadDomain()
                ,				stream.ReadUInt32()
                ,				stream.ReadUInt32()
                ,				stream.ReadUInt32()
                ,				stream.ReadUInt32()
                ,				stream.ReadUInt32()
			);
		}
	}
    public class CNAMEResource : IDNSResource 
    {        
	    public ResourceRecordType Type
	    {
		    get { return ResourceRecordType.CANONICAL_NAME; }
	    }

	    public readonly Domain Alias;
        public CNAMEResource(Domain Alias_) {
		    Alias = Alias_;
		}

		public override bool Equals(object other)
		{

			if (!(other is IDNSResource))
			{
				return false;
			}

			return Equals((IDNSResource)other);
		}

		public bool Equals(IDNSResource other)
		{
			if (!(other is CNAMEResource))
			{
				return false;
			}

			var other_resource = (CNAMEResource)other;
			return
                 this.Alias == other_resource.Alias 
            ;
		}

		public override int GetHashCode()
		{
			unchecked {
				int hash = 17;
                
                hash = hash * 29 + Alias.GetHashCode();
				return hash;
			}
		}

		public override string ToString()
		{
			string buffer = "CNAME[";
            buffer += " Alias=" + Alias;
			return buffer + "]";
		}

		public void Serialize(DNSOutputStream stream)
		{
          stream.WriteDomain(Alias);
		}

		public static CNAMEResource Unserialize(DNSInputStream stream, UInt16 size)
		{
			return new CNAMEResource(
				stream.ReadDomain()
			);
		}
	}
    public class MXResource : IDNSResource 
    {        
	    public ResourceRecordType Type
	    {
		    get { return ResourceRecordType.MAIL_EXCHANGE; }
	    }

	    public readonly UInt16 Preference;
	    public readonly Domain Mailserver;
        public MXResource(UInt16 Preference_,Domain Mailserver_) {
		    Preference = Preference_;
		    Mailserver = Mailserver_;
		}

		public override bool Equals(object other)
		{

			if (!(other is IDNSResource))
			{
				return false;
			}

			return Equals((IDNSResource)other);
		}

		public bool Equals(IDNSResource other)
		{
			if (!(other is MXResource))
			{
				return false;
			}

			var other_resource = (MXResource)other;
			return
                 this.Preference == other_resource.Preference 
                   &&                  this.Mailserver == other_resource.Mailserver 
            ;
		}

		public override int GetHashCode()
		{
			unchecked {
				int hash = 17;
                
                hash = hash * 29 + Preference.GetHashCode();                
                hash = hash * 29 + Mailserver.GetHashCode();
				return hash;
			}
		}

		public override string ToString()
		{
			string buffer = "MX[";
            buffer += " Preference=" + Preference;
            buffer += " Mailserver=" + Mailserver;
			return buffer + "]";
		}

		public void Serialize(DNSOutputStream stream)
		{
          stream.WriteUInt16(Preference);
          stream.WriteDomain(Mailserver);
		}

		public static MXResource Unserialize(DNSInputStream stream, UInt16 size)
		{
			return new MXResource(
				stream.ReadUInt16()
                ,				stream.ReadDomain()
			);
		}
	}
    public class PTRResource : IDNSResource 
    {        
	    public ResourceRecordType Type
	    {
		    get { return ResourceRecordType.POINTER; }
	    }

	    public readonly Domain Pointer;
        public PTRResource(Domain Pointer_) {
		    Pointer = Pointer_;
		}

		public override bool Equals(object other)
		{

			if (!(other is IDNSResource))
			{
				return false;
			}

			return Equals((IDNSResource)other);
		}

		public bool Equals(IDNSResource other)
		{
			if (!(other is PTRResource))
			{
				return false;
			}

			var other_resource = (PTRResource)other;
			return
                 this.Pointer == other_resource.Pointer 
            ;
		}

		public override int GetHashCode()
		{
			unchecked {
				int hash = 17;
                
                hash = hash * 29 + Pointer.GetHashCode();
				return hash;
			}
		}

		public override string ToString()
		{
			string buffer = "PTR[";
            buffer += " Pointer=" + Pointer;
			return buffer + "]";
		}

		public void Serialize(DNSOutputStream stream)
		{
          stream.WriteDomain(Pointer);
		}

		public static PTRResource Unserialize(DNSInputStream stream, UInt16 size)
		{
			return new PTRResource(
				stream.ReadDomain()
			);
		}
	}
}