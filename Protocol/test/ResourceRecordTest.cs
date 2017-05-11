using System;
using System.IO;
using System.Net;
using NUnit.Framework;

namespace DNSProtocol
{
    [TestFixture]
    public class CompressonContextTest
    {
        /**
         * Ensure that the compression context formats domain names correctly
         * the first time they are referenced.
         */
        [Test]
        public void TestCompressionFirstUse()
        {
            var compress = new CompressionOutputContext();
            var domain = new Domain("example.com");
            var domain_bytes = compress.SerializeDomain(domain);

            var expected = new byte[]
            {
                7, // Length of 'example'
                101,
                120,
                97,
                109,
                112,
                108,
                101,
                3, // Length of com
                99,
                111,
                109,
                0 // Length of root
            };

            Assert.That(domain_bytes, Is.EqualTo(expected));
        }

        /**
         * Ensure that the compression context can compress domains it has
         * already seen.
         */
        [Test]
        public void TestCompressionMultipleUses()
        {
            var compress = new CompressionOutputContext();
            var domain_stream = new MemoryStream();

            var domain = new Domain("example.com");
            var domain_bytes = compress.SerializeDomain(domain);
            domain_stream.Write(domain_bytes, 0, domain_bytes.Length);

            domain = new Domain("hostmaster.example.com");
            domain_bytes = compress.SerializeDomain(domain);
            domain_stream.Write(domain_bytes, 0, domain_bytes.Length);

            var expected = new byte[]
            {
                7, // Length of 'example'
                101,
                120,
                97,
                109,
                112,
                108,
                101,
                3, // Length of com
                99,
                111,
                109,
                0, // Length of root
                10, // Length of hostmaster
                104,
                111,
                115,
                116,
                109,
                97,
                115,
                116,
                101,
                114,
                // Two bytes worth of pointer - they should have 1 at the 15th
                // bit and the 14th bit on the MSB, and the LSB should be 0
                // (since that is the offset of the start of the example.com
                // domain
                192,
                0
            };

            Assert.That(domain_stream.ToArray(), Is.EqualTo(expected));
        }

        /**
         * Ensure that the compression context can compress parts of domains
         * it has already seen.
         */
        [Test]
        public void TestCompressionMultiplePartialUses()
        {
            var compress = new CompressionOutputContext();
            var domain_stream = new MemoryStream();

            var domain = new Domain("example.com");
            var domain_bytes = compress.SerializeDomain(domain);
            domain_stream.Write(domain_bytes, 0, domain_bytes.Length);

            domain = new Domain("hostmaster.com");
            domain_bytes = compress.SerializeDomain(domain);
            domain_stream.Write(domain_bytes, 0, domain_bytes.Length);

            var expected = new byte[]
            {
                7, // Length of 'example'
                101,
                120,
                97,
                109,
                112,
                108,
                101,
                3, // Length of com
                99,
                111,
                109,
                0, // Length of root
                10, // Length of hostmaster
                104,
                111,
                115,
                116,
                109,
                97,
                115,
                116,
                101,
                114,
                // Two bytes worth of pointer - they should have 1 at the 15th
                // bit and the 14th bit on the MSB, and the LSB should be 8
                // (since that is the offset of the start of the com domain)
                192,
                8
            };

            Assert.That(domain_stream.ToArray(), Is.EqualTo(expected));
        }

        /**
         * Ensure that the compression context outputs lower-case domain names.
         */
        [Test]
        public void TestCompressionLowerCase()
        {
            var compress = new CompressionOutputContext();
            var domain = new Domain("EXAMPLE.COM");
            var domain_bytes = compress.SerializeDomain(domain);
            compress.MoveForward((UInt16)domain_bytes.Length);

            var expected = new byte[]
            {
                7, // Length of 'example'
                101,
                120,
                97,
                109,
                112,
                108,
                101,
                3, // Length of com
                99,
                111,
                109,
                0 // Length of root
            };

            Assert.That(domain_bytes, Is.EqualTo(expected));
        }

        /**
          * Ensure that the input compression context can handle uncompressed
          * domain names.
         */
        [Test]
        public void TestUncompressionFirstUse()
        {
            var compress = new CompressionInputContext(new byte[]
                {
                    7, // Length of 'example'
                    101,
                    120,
                    97,
                    109,
                    112,
                    108,
                    101,
                    3, // Length of com
                    99,
                    111,
                    109,
                    0 // Length of root
                });
            Tuple<Domain, int> domain_info = compress.ReadDomainAt(0);

            var expected_domain = new Domain("example.com");
            var expected_offset = 13;
            Assert.That(domain_info.Item1, Is.EqualTo(expected_domain));
            Assert.That(domain_info.Item2, Is.EqualTo(expected_offset));
        }

        /**
         * Ensure that the input compression context can handle compressed
         * domain names.
         */
        [Test]
        public void TestUncompressionPointer()
        {
            var compress = new CompressionInputContext(new byte[]
                {
                    7, // Length of 'example'
                    101,
                    120,
                    97,
                    109,
                    112,
                    108,
                    101,
                    3, // Length of com
                    99,
                    111,
                    109,
                    0, // Length of root
                    3, // Length of dns
                    100,
                    110,
                    115,
                    // Pointer to start of context
                    192,
                    0
                });
            Tuple<Domain, int> domain_info = compress.ReadDomainAt(13);

            var expected_domain = new Domain("dns.example.com");
            var expected_offset = 19;
            Assert.That(domain_info.Item1, Is.EqualTo(expected_domain));
            Assert.That(domain_info.Item2, Is.EqualTo(expected_offset));
        }
    }

    [TestFixture]
    public class ResourceRecordSerializeTest
    {
        public DNSInputStream DNSInput(byte[] bytes)
        {
            var compress = new CompressionInputContext(bytes);
            var memory = new MemoryStream(bytes);
            return new DNSInputStream(memory, compress);
        }

        public Tuple<MemoryStream, DNSOutputStream> DNSOutput()
        {
            var compress = new CompressionOutputContext();
            var memory = new MemoryStream();
            var stream = new DNSOutputStream(memory, compress);
            return Tuple.Create(memory, stream);
        }

        [Test]
        public void TestSerializeAResource()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
			var record = new AResource(IPv4Address.Parse("192.168.0.1"));
            record.Serialize(out_info.Item2);
            var record_bytes = out_info.Item1.ToArray();

            var expected = new byte[] { 192, 168, 0, 1 };
            Assert.That(record_bytes, Is.EqualTo(expected));
        }

        [Test]
        public void TestUnserializeAResource()
        {
            var stream = DNSInput(new byte[] { 192, 168, 0, 1 });
            var resource = AResource.Unserialize(stream, 4);

			var expected = new AResource(IPv4Address.Parse("192.168.0.1"));
            Assert.That(resource, Is.EqualTo(expected));
        }

        [Test]
        public void TestSerializeCompleteAResource()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
			var record = new DNSRecord(
				new Domain("example.com"),
				AddressClass.INTERNET,
				42,
				new AResource(IPv4Address.Parse("192.168.0.1")));
			
            record.Serialize(out_info.Item2);
            var record_bytes = out_info.Item1.ToArray();

            var expected = new byte[]
            {
                7, // Length of example
                101,
                120,
                97,
                109,
                112,
                108,
                101,
                3, // Length of com
                99,
                111,
                109,
                0,
                // A record has code 1
                0,
                1,
                // INTERNET has class 1
                0,
                1,
                // Big-endian representation of 42
                0,
                0,
                0,
                42,
                // Record is 4 bytes long
                0,
                4,
                // The record itself
                192,
                168,
                0,
                1
            };
            Assert.That(record_bytes, Is.EqualTo(expected));
        }

        [Test]
        public void TestSerializeNSRecord()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
			var record = new NSResource(new Domain("dns.example.com"));
            record.Serialize(out_info.Item2);
            var record_bytes = out_info.Item1.ToArray();

            var expected = new byte[]
            {
                3, // Length of dns
                100,
                110,
                115,
                7, // Length of example
                101,
                120,
                97,
                109,
                112,
                108,
                101,
                3, // Length of com
                99,
                111,
                109,
                0
            };

            Assert.That(record_bytes, Is.EqualTo(expected));
        }

        [Test]
        public void TestUnserializeNSResource()
        {
            var stream = DNSInput(new byte[] { 3, 100, 110, 115, 7, 101, 120, 97, 109, 112, 108, 101, 3, 99, 111, 109, 0 });
            var resource = NSResource.Unserialize(stream, 4);

			var expected = new NSResource(new Domain("dns.example.com"));
            Assert.That(resource, Is.EqualTo(expected));
        }

        [Test]
        public void TestSerializeCompleteNSRecord()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
			var record = new DNSRecord(
				new Domain("example.com"),
				AddressClass.INTERNET,
				42,
				new NSResource(new Domain("dns.example.com")));
			
            record.Serialize(out_info.Item2);
            var record_bytes = out_info.Item1.ToArray();

            var expected = new byte[]
            {
                7, // Length of example
                101,
                120,
                97,
                109,
                112,
                108,
                101,
                3, // Length of com
                99,
                111,
                109,
                0,
                // NS record has code 2
                0,
                2,
                // INTERNET has class 1
                0,
                1,
                // Big-endian representation of 42
                0,
                0,
                0,
                42,
                // Record is 6 bytes long
                0,
                6,
                // The record itself - dns + a pointer to example.com
                3, // Length of dns
                100,
                110,
                115,
                // Pointer to example.com at byte 0
                192,
                0
            };
            Assert.That(record_bytes, Is.EqualTo(expected));
        }

        [Test]
        public void TestSerializeSOAResource()
        {
			UInt32 one_hour = 60 * 60;
			UInt32 one_day = one_hour * 24;
			UInt32 one_week = one_day * 7;

            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
			var record = new SOAResource(
				new Domain("dns.example.com"),
				new Domain("hostmaster.example.com"),
				42,
				one_week,
				one_hour,
				one_week,
				one_day);

            record.Serialize(out_info.Item2);
            var record_bytes = out_info.Item1.ToArray();

            var expected = new byte[]
            {
                3, // Length of dns
                100,
                110,
                115,
                7, // Length of example
                101,
                120,
                97,
                109,
                112,
                108,
                101,
                3, // Length of com
                99,
                111,
                109,
                0,
                10, // Length of hostmaster
                104,
                111,
                115,
                116,
                109,
                97,
                115,
                116,
                101,
                114,
                // Reference to byte 4, 'example.com'
                192,
                4,
                // Big-endian 42
                0,
                0,
                0,
                42,
                // Big-endian 604,800
                0,
                9,
                58,
                128,
                // Big-endian 3,600
                0,
                0,
                14,
                16,
                // Big-endian 604,800
                0,
                9,
                58,
                128,
                // Big-endian 86,400
                0,
                1,
                81,
                128
            };

            Assert.That(record_bytes, Is.EqualTo(expected));
        }

        [Test]
        public void TestUnserializeSOAResource()
        {
            var stream = DNSInput(new byte[]
                {
                    3, 100, 110, 115,
                    7, 101, 120, 97, 109, 112, 108, 101,
                    3, 99, 111, 109,
                    0,
                    10, 104, 111, 115, 116, 109, 97, 115, 116, 101, 114,
                    192, 4,
                    0, 0, 0, 42,
                    0, 9, 58, 128,
                    0, 0, 14, 16,
                    0, 9, 58, 128,
                    0, 1, 81, 128
                });
            var resource = SOAResource.Unserialize(stream, 4);

			UInt32 one_hour = 60 * 60;
			UInt32 one_day = one_hour * 24;
			UInt32 one_week = one_day * 7;

			var expected = new SOAResource(
				new Domain("dns.example.com"),
				new Domain("hostmaster.example.com"),
				42,
				one_week,
				one_hour,
				one_week,
				one_day);

            Assert.That(resource, Is.EqualTo(expected));
        }

        [Test]
        public void TestSerializeCompleteSOAResource()
        {
			UInt32 one_hour = 60 * 60;
			UInt32 one_day = one_hour * 24;
			UInt32 one_week = one_day * 7;

            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
			var record = new DNSRecord(
				new Domain("example.com"),
				AddressClass.INTERNET,
				42,
				new SOAResource(
					new Domain("dns.example.com"),
					new Domain("hostmaster.example.com"),
					42,
					one_week,
					one_hour,
					one_week,
					one_day));
			
            record.Serialize(out_info.Item2);
            var record_bytes = out_info.Item1.ToArray();

            var expected = new byte[]
            {
                7, // Length of example
                101,
                120,
                97,
                109,
                112,
                108,
                101,
                3, // Length of com
                99,
                111,
                109,
                0,
                // SOA record has code 6
                0,
                6,
                // INTERNET has class 1
                0,
                1,
                // Big-endian representation of 42
                0,
                0,
                0,
                42,
                // Record is 35 bytes long
                0,
                39,
                3, // Length of dns
                100,
                110,
                115,
                // Reference to byte 0, 'example.com'
                192,
                0,
                10, // Length of hostmaster
                104,
                111,
                115,
                116,
                109,
                97,
                115,
                116,
                101,
                114,
                // Reference to byte 0, 'example.com'
                192,
                0,
                // Big-endian 42
                0,
                0,
                0,
                42,
                // Big-endian 604,800
                0,
                9,
                58,
                128,
                // Big-endian 3,600
                0,
                0,
                14,
                16,
                // Big-endian 604,800
                0,
                9,
                58,
                128,
                // Big-endian 86,400
                0,
                1,
                81,
                128
            };

            Assert.That(record_bytes, Is.EqualTo(expected));
        }

        [Test]
        public void TestSerializeCNAMERecord()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
			var record = new CNAMEResource(new Domain("www.example.com"));
            record.Serialize(out_info.Item2);
            var record_bytes = out_info.Item1.ToArray();

            var expected = new byte[]
            {
                3, // Length of www
                119,
                119,
                119,
                7, // Length of example
                101,
                120,
                97,
                109,
                112,
                108,
                101,
                3, // Length of com
                99,
                111,
                109,
                0
            };

            Assert.That(record_bytes, Is.EqualTo(expected));
        }

        [Test]
        public void TestUnserializeCNAMEResource()
        {
            var stream = DNSInput(new byte[] { 3, 119, 119, 119, 7, 101, 120, 97, 109, 112, 108, 101, 3, 99, 111, 109, 0 });
            var resource = CNAMEResource.Unserialize(stream, 4);

			var expected = new CNAMEResource(new Domain("www.example.com"));
            Assert.That(resource, Is.EqualTo(expected));
        }

        [Test]
        public void TestSerializeCompleteCNAMERecord()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
			var record = new DNSRecord(
				new Domain("example.com"),
				AddressClass.INTERNET,
				42,
				new CNAMEResource(new Domain("www.example.com")));
			
            record.Serialize(out_info.Item2);
            var record_bytes = out_info.Item1.ToArray();

            var expected = new byte[]
            {
                7, // Length of example
                101,
                120,
                97,
                109,
                112,
                108,
                101,
                3, // Length of com
                99,
                111,
                109,
                0,
                // CNAME record has code 5
                0,
                5,
                // INTERNET has class 1
                0,
                1,
                // Big-endian representation of 42
                0,
                0,
                0,
                42,
                // Record is 6 bytes long
                0,
                6,
                3, // Length of www
                119,
                119,
                119,
                // Reference to byte 0, 'example.com'
                192,
                0,
            };

            Assert.That(record_bytes, Is.EqualTo(expected));
        }

        [Test]
        public void TestSerializeMXRecord()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
			var record = new MXResource(1000, new Domain("mail.example.com"));
            record.Serialize(out_info.Item2);
            var record_bytes = out_info.Item1.ToArray();

            var expected = new byte[]
            {
                // Big-endian 1,000
                3,
                232,
                4, // Length of mail
                109,
                97,
                105,
                108,
                7, // Length of example
                101,
                120,
                97,
                109,
                112,
                108,
                101,
                3, // Length of com
                99,
                111,
                109,
                0,
            };

            Assert.That(record_bytes, Is.EqualTo(expected));
        }

        [Test]
        public void TestUnserializeMXResource()
        {
            var stream = DNSInput(new byte[] { 3, 232, 4, 109, 97, 105, 108, 7, 101, 120, 97, 109, 112, 108, 101, 3, 99, 111, 109, 0 });
            var resource = MXResource.Unserialize(stream, 4);

			var expected = new MXResource(1000, new Domain("mail.example.com"));
            Assert.That(resource, Is.EqualTo(expected));
        }

        [Test]
        public void TestSerializeCompleteMXRecord()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
			var record = new DNSRecord(
				new Domain("example.com"),
				AddressClass.INTERNET,
				42,
				new MXResource(1000, new Domain("mail.example.com")));
			
            record.Serialize(out_info.Item2);
            var record_bytes = out_info.Item1.ToArray();

            var expected = new byte[]
            {
                7, // Length of example
                101,
                120,
                97,
                109,
                112,
                108,
                101,
                3, // Length of com
                99,
                111,
                109,
                0,
                // MX record has code 15
                0,
                15,
                // INTERNET has class 1
                0,
                1,
                // Big-endian representation of 42
                0,
                0,
                0,
                42,
                // Record is 6 bytes long
                0,
                9,
                // Big-endian 1,000
                3,
                232,
                4, // Length of mail
                109,
                97,
                105,
                108,
                // Pointer to byte 0, "example.com"
                192,
                0
            };

            Assert.That(record_bytes, Is.EqualTo(expected));
        }

        [Test]
        public void TestSerializePTRRecord()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
			var record = new PTRResource(new Domain("www.example.com"));
            record.Serialize(out_info.Item2);
            var record_bytes = out_info.Item1.ToArray();

			var expected = new byte[]
			{
				3, // Length of www
                119,
				119,
				119,
				7, // Length of example
                101,
				120,
				97,
				109,
				112,
				108,
				101,
				3, // Length of com
                99,
				111,
				109,
				0
			};

            Assert.That(record_bytes, Is.EqualTo(expected));
        }

        [Test]
        public void TestUnserializePTResource()
        {
            var stream = DNSInput(new byte[] {3, 119, 119, 119, 7, 101, 120, 97, 109, 112, 108, 101, 3, 99, 111, 109, 0});
            var resource = NSResource.Unserialize(stream, 4);

			var expected = new NSResource(new Domain("www.example.com"));
            Assert.That(resource, Is.EqualTo(expected));
        }

        [Test]
        public void TestSerializeCompletePTRRecord()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
			var record = new DNSRecord(
				new Domain("1.0.168.192.in-addr.arpa"),
				AddressClass.INTERNET,
				42,
				new PTRResource(new Domain("www.example.com")));
			
            record.Serialize(out_info.Item2);
            var record_bytes = out_info.Item1.ToArray();

            var expected = new byte[]
            {
				1, // Length of 1
				49,
				1, // Length of 0
				48,
				3, // Length of 168
				49,
				54,
				56,
				3, // Length of 192
				49,
				57,
				50,
				7, // Length of in-addr
				105,
				110,
				45,
				97,
				100,
				100,
				114,
				4, // Length of arpa
				97,
				114,
				112,
				97,
                0,
                // PTR record has code 12
                0,
                12,
                // INTERNET has class 1
                0,
                1,
                // Big-endian representation of 42
                0,
                0,
                0,
                42,
                // Record is 17 bytes long
                0,
                17,
                // The record itself - www.example.com
                3, // Length of www
                119,
                119,
                119,
				7, // Length of example
				101,
				120,
				97,
				109,
				112,
				108,
				101,
				3, // Length of com
				99,
				111,
				109,
				0
            };
            Assert.That(record_bytes, Is.EqualTo(expected));
        }
    }
}
