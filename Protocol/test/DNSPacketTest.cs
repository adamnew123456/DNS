using System;
using System.IO;
using System.Net;
using NUnit.Framework;

namespace DNSProtocol
{
    [TestFixture]
    public class DNSQuestionTest
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
        public void testSerializeDNSQuestion()
        {
            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();

			var question = new DNSQuestion(
				new Domain("example.com"),  
				ResourceRecordType.HOST_ADDRESS,  
				AddressClass.INTERNET);
			
            question.Serialize(out_info.Item2);
            var question_bytes = out_info.Item1.ToArray();

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
            };

            Assert.That(question_bytes, Is.EqualTo(expected));
        }

        [Test]
        public void testUnserializeDNSQuestion()
        {
            var stream = DNSInput(new byte[]
                {
                    7, 101, 120, 97, 109, 112, 108, 101,
                    3, 99, 111, 109,
                    0,
                    0, 1,
                    0, 1,
                });

            var question = DNSQuestion.Unserialize(stream);

			var expected = new DNSQuestion(
				new Domain("example.com"), 
				ResourceRecordType.HOST_ADDRESS, 
				AddressClass.INTERNET);

            Assert.That(question, Is.EqualTo(expected));
        }
    }

    [TestFixture]
    public class DNSPacketTest
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
        public void testSerializeDNSPacket()
        {
			var question = new DNSQuestion(
				new Domain("example.com"), 
				ResourceRecordType.HOST_ADDRESS, 
				AddressClass.INTERNET);

			var answer = new DNSRecord(
				new Domain("example.com"),
				AddressClass.INTERNET,
				42,
				new AResource(new IPAddress(new byte[] { 192, 168, 0, 1 })));

			var packet = new DNSPacket(
				42,
				true, QueryType.STANDARD_QUERY, true, false, false, true, ResponseType.NO_ERROR,
				new DNSQuestion[] { question }, new DNSRecord[] { answer }, new DNSRecord[0], new DNSRecord[0]);

            Tuple<MemoryStream, DNSOutputStream> out_info = DNSOutput();
            packet.Serialize(out_info.Item2);
            var packet_bytes = out_info.Item1.ToArray();

            var expected = new byte[]
            {
                // Big-endian 42
                0,
                42,
                // This is the query/resonse bit, the query type, the authority
                // bit, the truncation bit, and the recursion desired bit
                4,
                // This is the recursion available bit, the zero segment,
                // and the return code
                128,
                // Big-endian 1
                0,
                1,
                // Big-endian 1
                0,
                1,
                // Big-endian 0
                0,
                0,
                // Big-endian 0
                0,
                0,
                // The question - A record for example.com
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
                // The answer - the A record for example.com
                // Pointer to byte 96 - "example.com"
                192,
                12,
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

            Assert.That(packet_bytes, Is.EqualTo(expected));
        }


		[Test]
		public void testUnserializeDNSPacket()
		{

			var stream = DNSInput(new byte[]
				{
                    // Big-endian 42
                    0,
					42,
                    // This is the query/resonse bit, the query type, the authority
                    // bit, the truncation bit, and the recursion desired bit
                    4,
                    // This is the recursion available bit, the zero segment,
                    // and the return code
                    128,
                    // Big-endian 1
                    0,
					1,
                    // Big-endian 1
                    0,
					1,
                    // Big-endian 0
                    0,
					0,
                    // Big-endian 0
                    0,
					0,
                    // The question - A record for example.com
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
                    // The answer - the A record for example.com
                    // Pointer to byte 96 - "example.com"
                    192,
					12,
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
				});
			var packet = DNSPacket.Unserialize(stream);

			var question = new DNSQuestion(
				new Domain("example.com"),
				ResourceRecordType.HOST_ADDRESS,
				AddressClass.INTERNET);

			var answer = new DNSRecord(
				new Domain("example.com"),
				AddressClass.INTERNET,
				42,
				new AResource(new IPAddress(new byte[] { 192, 168, 0, 1 })));

			var expected = new DNSPacket(
				42,
				true, QueryType.STANDARD_QUERY, true, false, false, true, ResponseType.NO_ERROR,
				new DNSQuestion[] { question }, new DNSRecord[] { answer }, new DNSRecord[0], new DNSRecord[0]);

            Assert.That(packet, Is.EqualTo(expected));
        }
    }
}
