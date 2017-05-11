using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DNSProtocol
{
    /**
     * The different query types allowed by the DNS protocol.
     */
    public enum QueryType : byte
    {
        STANDARD_QUERY = 0,
        UNSUPPORTED = 15
    }

    /**
     * The different response types allowed by the DNS protocol.
     */
    public enum ResponseType : byte
    {
        NO_ERROR = 0,
        FORMAT_ERROR = 1,
        SERVER_FAILURE = 2,
        NAME_ERROR = 3,
        NOT_IMPLEMENTED = 4,
        REFUSE = 5,
        UNSUPPORTED = 15
    }

    /**
     * What kind of address is being requested (only Internet
     * addresses are supported)
     */
    public enum AddressClass : UInt16
    {
        INTERNET = 1,
        UNSUPPORTED = 0
    }

    /**
     * These are the resource records supported by this DNS
     * implementation.
     */
    public enum ResourceRecordType : UInt16
    {
        // An IPv4 host address; an A record
        HOST_ADDRESS = 1,

        // A name server which is the authority for another domain; an
        // NS record
        NAME_SERVER = 2,

        // An alias for a domain name; a CNAME record
        CANONICAL_NAME = 5,

        // Information about who is responsible for this name server;
        // an SOA record
        START_OF_AUTHORITY = 6,

        // A pointer to the SMTP server for a domain; an MX record
        MAIL_EXCHANGE = 15,

		// A pointer to a domain; unlike CNAMEs, these aren't aliases, but are used for reverse queries
		POINTER = 12,

        // A general code used for an unsupported record/query type.
        UNSUPPORTED = 0
    }


    /**
     * A set of methods to help handle the 'reserved' numbers case in the DNS
     * protocol - anything that we don't support is binned into the
     * 'UNSUPPORTED' category by passing it through the enum's Normalize
     * extension method.
     */
    public static class DNSEnumExtensions
    {
        public static QueryType Normalize(this QueryType query)
        {
            switch (query)
            {
                case QueryType.STANDARD_QUERY:
                    return query;
                default:
                    return QueryType.UNSUPPORTED;
            }
        }

        public static ResponseType Normalize(this ResponseType response)
        {

            switch (response)
            {
                case ResponseType.NO_ERROR:
                case ResponseType.NAME_ERROR:
                case ResponseType.FORMAT_ERROR:
                case ResponseType.SERVER_FAILURE:
                case ResponseType.REFUSE:
                case ResponseType.NOT_IMPLEMENTED:
                    return response;
                default:
                    return ResponseType.UNSUPPORTED;
            }
        }

        public static AddressClass Normalize(this AddressClass address)
        {
            switch (address)
            {
                case AddressClass.INTERNET:
                    return address;
                default:
                    return AddressClass.UNSUPPORTED;
            }
        }

        public static ResourceRecordType Normalize(this ResourceRecordType resource)
        {
            switch (resource)
            {
                case ResourceRecordType.CANONICAL_NAME:
                case ResourceRecordType.HOST_ADDRESS:
                case ResourceRecordType.MAIL_EXCHANGE:
                case ResourceRecordType.NAME_SERVER:
                case ResourceRecordType.START_OF_AUTHORITY:
				case ResourceRecordType.POINTER:
                    return resource;
                default:
                    return ResourceRecordType.UNSUPPORTED;
            }
        }
    }

    /**
     * The input compression context allows decoders to process both compressed
     * and uncompressed domain names.
     */
    public class CompressionInputContext
    {
        private byte[] packet;

        public CompressionInputContext(byte[] packet)
        {
            this.packet = packet;
        }

        /**
         * Reads a domain at the given offset in the packet, and returns the
         * domain and the offset just after the end of the domain.
         */
        public Tuple<Domain, int> ReadDomainAt(int offset)
        {
            var domain = new List<string>();
            int end_offset = -1;
            bool done = false;

            while (!done)
            {
                // If the top 2 bits of the size byte are 1, then we're dealing
                // with a compressed domain (regular domain segments can't be
                // of size >= 64)
                if ((packet[offset] & 0xc0) != 0)
                {
                    // We have to save this, because the offset will be
                    // redirected to resolve the compressed domain, and where it
                    // ends up will not be the end of *this* domain name
                    if (end_offset == -1)
                    {
                        end_offset = offset + 2;
                    }

                    offset = ((packet[offset] & 63) << 8) | packet[offset + 1];
                }
                else
                {
                    byte segment_length = packet[offset];
                    offset++;

                    if (segment_length > 0)
                    {
                        var chars = new char[segment_length];
                        for (int i = 0; i < segment_length; i++)
                        {
                            if (packet[offset + i] > 127)
                            {
                                throw new InvalidDataException("Domain names can only be ASCII, saw " + packet[offset + i] + " at " + (offset + i));
                            }

                            chars[i] = (char)packet[offset + i];
                        }

                        domain.Add(new String(chars));
                        offset += segment_length;
                        done = false;
                    }
                    else
                    {
                        domain.Add("");
                        done = true;
                    }
                }
            }

            // If we've never hit a pointer, this will be unassigned and point
            // to the end of the domain we care about
            if (end_offset == -1)
            {
                end_offset = offset;
            }

            return Tuple.Create(new Domain(domain), end_offset);
        }
    }

    /**
     * The output compression context provides for DNS hostname compression, by
     * allowing resource record encoders to register their domain names
     * and encode them via compression (if possible).
     */
    public class CompressionOutputContext
    {
        private UInt16 offset;
        private Dictionary<Domain, UInt16> domains_seen;

        public CompressionOutputContext()
        {
            offset = 0;
            domains_seen = new Dictionary<Domain, UInt16>();
        }

        /**
         * Moves the current offset pointer forward.
         */
        public void MoveForward(UInt16 incr)
        {
            offset += incr;
        }

        /**
         * Converts a standard domain name into a series of bytes, possibly
         * compressed using domains seen so far.
         */
        public byte[] SerializeDomain(Domain domain)
        {
            var stream = new MemoryStream();

            if (domain[domain.Length - 1] != "")
            {
                throw new InvalidDataException("Domain name must end in 0 byte segment");
            }

            var idx = 0;
            foreach (var segment in domain)
            {
                Domain current_subdomain = domain.Slice(idx);

                // Avoid compressing on an empty domain, since it wastes a byte
                // of space and makes the decoder work harder for no reason
                if (segment != "" &&
                    domains_seen.ContainsKey(current_subdomain))
                {
                    // RFC specifies that top-2 bits must be 1
                    UInt16 compress_bits = (1 << 15) + (1 << 14);
                    UInt16 offset_bits = domains_seen[current_subdomain];
                    UInt16 compress_ptr = (UInt16)(compress_bits | offset_bits);
                    stream.WriteByte((byte)((compress_ptr >> 8) & 0xff));
                    stream.WriteByte((byte)(compress_ptr & 0xff));
                    MoveForward(2);
                    break;
                }
                else
                {
                    // Record the current domain for later compression, and
                    // then write it out (but only if it'll fit into the
                    // 14 bits we have to store the pointer in)
                    if (offset <= (1 << 14) - 1)
                    {
                        domains_seen[current_subdomain] = offset;
                    }

                    var segment_bytes = new byte[segment.Length];
                    for (int i = 0; i < segment.Length; i++)
                    {
                        segment_bytes[i] = (byte)segment[i];
                    }

                    stream.WriteByte((byte)segment_bytes.Length);
                    stream.Write(segment_bytes, 0, segment_bytes.Length);
                    MoveForward((UInt16)(1 + segment_bytes.Length));
                    idx++;
                }
            }

            return stream.ToArray();
        }
    }

    /**
     * A single member of a field group.
     */
    public class Field
    {
        public readonly int Size;
        public byte Value;

        // The "pack" constructor needs a value, so that Pack() will have
        // something to pack with
        public Field(int size, byte value)
        {
            Size = size;
            Value = value;
        }

        // The "unpack" constructor doesn't provide a value, since Unpack()
        // will fill it in from the data
        public Field(int size)
        {
            Size = size;
            Value = 0;
        }
    }

    /**
     * A configuration of several values which are stored in regions of the
     * same byte.
     */
    public class FieldGroup
    {
        private List<Field> fields;

        public FieldGroup()
        {
            fields = new List<Field>();
        }

        /**
         * Registers a new Field with the FieldGroup. Note that the order
         * matters, since the field will be put immediately after the end of
         * the previous field (starting at the most significant bit and going
         * down)
         */
        public FieldGroup Add(Field field)
        {
            fields.Add(field);
            return this;
        }

        /**
         * Computes the size of each of the registered fields. Must be equal to
         * 8 before packing/unpacking.
         */
        private int Size()
        {
            int x = 0;
            foreach (var field in fields)
            {
                x += field.Size;
            }

            return x;
        }

        /**
         * Packs the registered fields into a byte.
         */
        public byte Pack()
        {
            if (Size() != 8)
            {
                throw new InvalidDataException("FieldGroup must be exactly 1 byte long");
            }

            int offset = 8;
            byte value = 0;
            foreach (var field in fields)
            {
                offset -= field.Size;
                value |= (byte)(field.Value << offset);
            }

            return value;
        }

        /**
         * Unpacks the given byte into the registered fields.
         */
        public void Unpack(byte value)
        {
            if (Size() != 8)
            {
                throw new InvalidDataException("FieldGroup must be exactly 1 byte long");
            }

            var left_edge = 8;
            foreach (var field in fields)
            {
                var right_edge = left_edge - field.Size;
                var mask = 0;
                for (int i = right_edge; i < left_edge; i++)
                {
                    mask |= 1 << i;
                }

                field.Value = (byte)((value & mask) >> right_edge);
                left_edge = right_edge;
            }
        }
    }

    /**
     * A stream that provides the ability to read data in ways that DNS uses.
     */
    public class DNSInputStream
    {
        private Stream stream;
        private CompressionInputContext compress;

        public DNSInputStream(Stream stream, CompressionInputContext compress)
        {
            this.stream = stream;
            this.compress = compress;
        }

        /**
         * Reads a single byte from the input stream.
         */
        public byte ReadByte()
        {
            return (byte)stream.ReadByte();
        }

        /**
         * Reads a fixed number of bytes from the input stream.
         */
        public byte[] ReadBytes(int count)
        {
            var bytes = new byte[count];
            stream.Read(bytes, 0, count);
            return bytes;
        }

        /**
         * Reads a big-endian 16-bit integer from the input stream.
         */
        public UInt16 ReadUInt16()
        {
            var bytes = ReadBytes(2);
            return (UInt16)((bytes[0] << 8) + bytes[1]);
        }

        /**
         * Reads a big-endian 32-bit integer from the input stream.
         */
        public UInt32 ReadUInt32()
        {
            var bytes = ReadBytes(4);
            return (UInt32)((bytes[0] << 24) + (bytes[1] << 16) + (bytes[2] << 8) + bytes[3]);
        }

		/**
		 * Reads an IPv4 address from the input stream.
		 */
		public IPv4Address ReadIPv4Address()
		{
			return new IPv4Address(ReadBytes(4));
		}

		/**
		 * Reads an IPv6 address from the input stream.
		 */
		public IPv6Address ReadIPv6Address()
		{
			return new IPv6Address(ReadBytes(16));
		}

        /**
         * Reads a domain name from the input stream.
         */
        public Domain ReadDomain()
        {
            Tuple<Domain, int> domain_info = compress.ReadDomainAt((int)stream.Position);
            stream.Seek(domain_info.Item2, SeekOrigin.Begin);
            return domain_info.Item1;
        }
    }

    /**
     * Holes, as used by the output stream, are a way to move the output stream
     * forward and make room for a value without the value having to exist
     * first. Filling the hole means putting the value back into the stream at
     * the point the stream was at when the hole was created.
     */
    public interface IOutputHole<T>
    {
        void Fill(T value);
    }

    /**
     * A stream that provides the ability to write data in ways that DNS uses.
     */
    public class DNSOutputStream
    {
        private Stream stream;
        private CompressionOutputContext compress;

        public DNSOutputStream(Stream stream, CompressionOutputContext compress)
        {
            this.stream = stream;
            this.compress = compress;
        }

        public long Position
        {
            get { return stream.Position; }
        }

        /**
         * Alters the endianness of its input (assuming that its input is
         * a single value, and not, say, a pair of 16-bit integers) so that it
         * conforms to the network byte order.
         */
        private void ToNetworkEndianness(byte[] bytes)
        {
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
        }

        /**
         * Writes a single byte to the output stream.
         */
        public void WriteByte(byte value)
        {
            stream.WriteByte(value);
            compress.MoveForward(1);
        }

        /**
         * Writes a fixed number of bytes to the output stream.
         */
        public void WriteBytes(byte[] bytes)
        {
            stream.Write(bytes, 0, bytes.Length);
            compress.MoveForward((UInt16)bytes.Length);
        }

        /**
         * Writes a fixed number of bytes to the output stream without moving
         * the compression stream ahead.
         */
        public void WriteBytesNoForward(byte[] bytes)
        {
            stream.Write(bytes, 0, bytes.Length);
        }

        /**
         * Writes a big-endian 16-bit integer to the output stream.
         */
        public void WriteUInt16(UInt16 value)
        {
            var bytes = BitConverter.GetBytes(value);
            ToNetworkEndianness(bytes);
            WriteBytes(bytes);
        }

        /**
         * Writes a big-endian 32-bit integer to the output stream.
         */
        public void WriteUInt32(UInt32 value)
        {
            var bytes = BitConverter.GetBytes(value);
            ToNetworkEndianness(bytes);
            WriteBytes(bytes);
        }

		/**
		 * Writes an IPv4 address to the input stream.
		 */
		public void WriteIPv4Address(IPv4Address address)
		{
			WriteBytes(address.GetAddressBytes());
		}

		/**
		 * Writes an IPv6 address to the input stream.
		 */
		public void WriteIPv6Address(IPv6Address address)
		{
			WriteBytes(address.GetAddressBytes());
		}

        private class ByteHole : IOutputHole<byte>
        {
            private Stream stream;
            private long offset;

            public ByteHole(Stream stream, long offset)
            {
                this.stream = stream;
                this.offset = offset;
            }

            public void Fill(byte value)
            {
                var saved_posn = stream.Position;
                stream.Seek(offset, SeekOrigin.Begin);
                stream.WriteByte(value);
                stream.Seek(saved_posn, SeekOrigin.Begin);
            }
        }

        private class UInt16Hole : IOutputHole<UInt16>
        {
            private Stream stream;
            private long offset;

            public UInt16Hole(Stream stream, long offset)
            {
                this.stream = stream;
                this.offset = offset;
            }

            public void Fill(UInt16 value)
            {
                var bytes = BitConverter.GetBytes(value);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }

                var saved_posn = stream.Position;
                stream.Seek(offset, SeekOrigin.Begin);
                stream.Write(bytes, 0, bytes.Length);
                stream.Seek(saved_posn, SeekOrigin.Begin);
            }
        }

        private class UInt32Hole : IOutputHole<UInt32>
        {
            private Stream stream;
            private long offset;

            public UInt32Hole(Stream stream, long offset)
            {
                this.stream = stream;
                this.offset = offset;
            }

            public void Fill(UInt32 value)
            {
                var bytes = BitConverter.GetBytes(value);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }

                var saved_posn = stream.Position;
                stream.Seek(offset, SeekOrigin.Begin);
                stream.Write(bytes, 0, bytes.Length);
                stream.Seek(saved_posn, SeekOrigin.Begin);
            }
        }

        /**
         * Creates a hole representing a byte, which can be filled in. This is
         * typically useful for encoding lengths of things that are generated
         * later.
         */
        public IOutputHole<byte> WriteByteHole()
        {
            var offset = stream.Position;
            WriteByte(0);
            return new ByteHole(stream, offset);
        }

        /**
         * Creates a hole representing an unsigned 16-bit integer, which can
         * be filled in. This is typically useful for encoding lengths of
         * things that are generated later.
         */
        public IOutputHole<UInt16> WriteUInt16Hole()
        {
            var offset = stream.Position;
            WriteUInt16(0);
            return new UInt16Hole(stream, offset);
        }

        /**
         * Creates a hole representing an unsigned 32-bit integer, which can
         * be filled in. This is typically useful for encoding lengths of
         * things that are generated later.
         */
        public IOutputHole<UInt32> WriteUInt32Hole()
        {
            var offset = stream.Position;
            WriteUInt32(0);
            return new UInt32Hole(stream, offset);
        }

        /**
         * Writes a domain name to the output stream, possibly compressing it.
         */
        public void WriteDomain(Domain domain)
        {
            WriteBytesNoForward(compress.SerializeDomain(domain));
        }
    }

	/**
	 * A wrapper around IPAddress that allows only IPv4 addresses.
	 */
	public class IPv4Address: IPAddress
	{
		public IPv4Address(int new_address): base((long)new_address)
		{
			if (AddressFamily != AddressFamily.InterNetwork)
			{
				throw new ArgumentException(this + " is not a valid IPv4 address");
			}
		}

		public IPv4Address(byte[] address) : base(address)
		{
			if (AddressFamily != AddressFamily.InterNetwork)
			{
				throw new ArgumentException(this + " is not a valid IPv4 address");
			}
		}

		public IPv4Address(IPAddress address) : this(address.GetAddressBytes())
		{
		}

		public static new IPv4Address Parse(string address)
		{
			return new IPv4Address(IPAddress.Parse(address));
		}
	}
}
