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
        public void testCompressionFirstUse()
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
        public void testCompressionMultipleUses()
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
        public void testCompressionMultiplePartialUses()
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
        public void testCompressionLowerCase()
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
        public void testUncompressionFirstUse()
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
        public void testUncompressionPointer()
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
        public void testSerializeAResource()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
            var record = new AResource();
            record.Address = new IPAddress(new byte[] { 192, 168, 0, 1 });
            record.Serialize(out_info.Item2);
            var record_bytes = out_info.Item1.ToArray();

            var expected = new byte[] { 192, 168, 0, 1 };
            Assert.That(record_bytes, Is.EqualTo(expected));
        }

        [Test]
        public void testUnserializeAResource()
        {
            var stream = DNSInput(new byte[] { 192, 168, 0, 1 });
            var resource = AResource.Unserialize(stream, 4);

            var expected = new AResource();
            expected.Address = new IPAddress(new byte[] { 192, 168, 0, 1 });
            Assert.That(resource, Is.EqualTo(expected));
        }

        [Test]
        public void testSerializeCompleteAResource()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
            var record = new DNSRecord();
            var record_info = new AResource();
            record.Name = new Domain("example.com");
            record.AddressClass = AddressClass.INTERNET;
            record.TimeToLive = 42;
            record.Resource = record_info;
            record_info.Address = new IPAddress(new byte[] { 192, 168, 0, 1 });
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
        public void testSerializeNSRecord()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
            var record = new NSResource();
            record.Nameserver = new Domain("dns.example.com");
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
        public void testUnserializeNSResource()
        {
            var stream = DNSInput(new byte[] { 3, 100, 110, 115, 7, 101, 120, 97, 109, 112, 108, 101, 3, 99, 111, 109, 0 });
            var resource = NSResource.Unserialize(stream, 4);

            var expected = new NSResource();
            expected.Nameserver = new Domain("dns.example.com");
            Assert.That(resource, Is.EqualTo(expected));
        }

        [Test]
        public void testSerializeCompleteNSRecord()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
            var record = new DNSRecord();
            var record_info = new NSResource();
            record.Name = new Domain("example.com");
            record.AddressClass = AddressClass.INTERNET;
            record.TimeToLive = 42;
            record.Resource = record_info;
            record_info.Nameserver = new Domain("dns.example.com");
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
        public void testSerializeSOAResource()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
            var record = new SOAResource();
            record.PrimaryNameServer = new Domain("dns.example.com");
            record.Hostmaster = new Domain("hostmaster.example.com");
            record.Serial = 42;
            record.RefreshSeconds = 60 * 60 * 24 * 7; // 1 week of seconds
            record.RetrySeconds = 60 * 60; // 1 hour of seconds
            record.ExpireSeconds = 60 * 60 * 24 * 7; // 1 week of seconds
            record.MinimumTTL = 60 * 60 * 24; // 1 day of seconds
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
        public void testUnserializeSOAResource()
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

            var expected = new SOAResource();
            expected.PrimaryNameServer = new Domain("dns.example.com");
            expected.Hostmaster = new Domain("hostmaster.example.com");
            expected.Serial = 42;
            expected.RefreshSeconds = 60 * 60 * 24 * 7; // 1 week of seconds
            expected.RetrySeconds = 60 * 60; // 1 hour of seconds
            expected.ExpireSeconds = 60 * 60 * 24 * 7; // 1 week of seconds
            expected.MinimumTTL = 60 * 60 * 24; // 1 day of seconds
            Assert.That(resource, Is.EqualTo(expected));
        }

        [Test]
        public void testSerializeCompleteSOAResource()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
            var record = new DNSRecord();
            var record_info = new SOAResource();
            record.Name = new Domain("example.com");
            record.AddressClass = AddressClass.INTERNET;
            record.TimeToLive = 42;
            record.Resource = record_info;
            record_info.PrimaryNameServer = new Domain("dns.example.com");
            record_info.Hostmaster = new Domain("hostmaster.example.com");
            record_info.Serial = 42;
            record_info.RefreshSeconds = 60 * 60 * 24 * 7; // 1 week of seconds
            record_info.RetrySeconds = 60 * 60; // 1 hour of seconds
            record_info.ExpireSeconds = 60 * 60 * 24 * 7; // 1 week of seconds
            record_info.MinimumTTL = 60 * 60 * 24;
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
        public void testSerializeCNAMERecord()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
            var record = new CNAMEResource();
            record.Alias = new Domain("www.example.com");
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
        public void testUnserializeCNAMEResource()
        {
            var stream = DNSInput(new byte[] { 3, 119, 119, 119, 7, 101, 120, 97, 109, 112, 108, 101, 3, 99, 111, 109, 0 });
            var resource = CNAMEResource.Unserialize(stream, 4);

            var expected = new CNAMEResource();
            expected.Alias = new Domain("www.example.com");
            Assert.That(resource, Is.EqualTo(expected));
        }

        [Test]
        public void testSerializeCompleteCNAMERecord()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
            var record = new DNSRecord();
            var record_info = new CNAMEResource();
            record.Name = new Domain("example.com");
            record.AddressClass = AddressClass.INTERNET;
            record.TimeToLive = 42;
            record.Resource = record_info;
            record_info.Alias = new Domain("www.example.com");
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
        public void testSerializeMXRecord()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
            var record = new MXResource();
            record.Preference = 1000;
            record.Mailserver = new Domain("mail.example.com");
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
        public void testUnserializeMXResource()
        {
            var stream = DNSInput(new byte[] { 3, 232, 4, 109, 97, 105, 108, 7, 101, 120, 97, 109, 112, 108, 101, 3, 99, 111, 109, 0 });
            var resource = MXResource.Unserialize(stream, 4);

            var expected = new MXResource();
            expected.Preference = 1000;
            expected.Mailserver = new Domain("mail.example.com");
            Assert.That(resource, Is.EqualTo(expected));
        }

        [Test]
        public void testSerializeCompleteMXRecord()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
            var record = new DNSRecord();
            var record_info = new MXResource();
            record.Name = new Domain("example.com");
            record.AddressClass = AddressClass.INTERNET;
            record.TimeToLive = 42;
            record.Resource = record_info;
            record_info.Preference = 1000;
            record_info.Mailserver = new Domain("mail.example.com");
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
    }
}
