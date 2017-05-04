using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DNSProtocol
{
    /**
     * A question that a client can ask the server, included in the
     * DNSPacket body.
     */
    public class DNSQuestion : IEquatable<DNSQuestion>
    {
        // The domain name which the client is inquiring about,
        // represented as a list of domain segments (with the last
        // one being an empty-length segment representing the root)
        public Domain Name;

        // The type of query that the client is making
        public ResourceRecordType QueryType;

        // The address format of the query (e.g. INternet)
        public AddressClass AddressClass;

		public DNSQuestion(Domain name, ResourceRecordType type, AddressClass address_class)
		{
			Name = name;
			QueryType = type;
			AddressClass = address_class;
		}

        public override bool Equals(object other)
        {
            if (!(other is DNSQuestion))
            {
                return false;
            }

            return Equals((DNSQuestion)other);
        }

        public override int GetHashCode()
        {
            // See SO:
            // http://stackoverflow.com/questions/263400/what-is-the-best-algorithm-for-an-overridden-system-object-gethashcode
            unchecked
            {
                int hash = 17;
                hash = hash * 29 + Name.GetHashCode();
                hash = hash * 29 + (int)QueryType;
                hash = hash * 29 + (int)AddressClass;
                return hash;
            }
        }

        public bool Equals(DNSQuestion other)
        {
            return this.Name == other.Name &&
            this.QueryType == other.QueryType &&
            this.AddressClass.Equals(other.AddressClass);
        }

        public override string ToString()
        {
            return "Question(" +
            "Name=" + Name + ", " +
            "Query=" + QueryType + ", " +
            "AddressClass=" + AddressClass +
            ")";
        }

        public void Serialize(DNSOutputStream stream)
        {
            stream.WriteDomain(Name);
            stream.WriteUInt16((UInt16)QueryType);
            stream.WriteUInt16((UInt16)AddressClass);
        }

        public static DNSQuestion Unserialize(DNSInputStream stream)
        {
            var name = stream.ReadDomain();
			var query_type = (ResourceRecordType)stream.ReadUInt16();
            var address_class = (AddressClass)stream.ReadUInt16();
			return new DNSQuestion(name, query_type.Normalize(), address_class.Normalize());
        }
    }

    /**
     * A DNSPacket represents the structure of a DNS packet on the
     * wire, and is responsible for serializing/unserializing DNS
     * requests and responses.
     */
    public class DNSPacket : IEquatable<DNSPacket>
    {
        // DNS requires packet identifiers, since it sends
        // information over UDP and can't rely on connections to
        // distinguish one client from another.
        public UInt16 Id;

        // Whether this is a query or a response.
        public bool IsQuery;

        // The kind of query that this message is making.
        public QueryType QueryType;

        // Whether or not the response being given is authoritative
        public bool IsAuthority;

        // Whether or not the reqponse was truncated
        public bool IsTruncated;

        // Whether the name server *should* act recursively or not
        // (Set by the client to direct the server)
        public bool RecursiveRequest;

        // Whether the name server *will* act recursively or not
        // (Set by the server to inform the client)
        public bool RecursiveResponse;

        // How the server is responding to the user's request
        public ResponseType ResponseType;

        // The queries that make up the question section
        public DNSQuestion[] Questions;

        // The responses that make up the answer section
        public DNSRecord[] Answers;

        // The responses that make up the authority section
        public DNSRecord[] AuthoritativeAnswers;

        // The responses that make up the additional records section
        public DNSRecord[] AdditionalRecords;

		public DNSPacket(UInt16 id, bool is_query, QueryType qtype, bool is_authority, bool is_truncated,
						 bool recurisve_request, bool recursive_response, ResponseType rtype,
						 IEnumerable<DNSQuestion> questions, IEnumerable<DNSRecord> answers,
						 IEnumerable<DNSRecord> authority, IEnumerable<DNSRecord> additional)
		{
			Id = id;
			IsQuery = is_query;
			QueryType = qtype;
			IsAuthority = is_authority;
			IsTruncated = is_truncated;
			RecursiveRequest = recurisve_request;
			RecursiveResponse = recursive_response;
			ResponseType = rtype;
			Questions = questions.ToArray();
			Answers = answers.ToArray();
			AuthoritativeAnswers = authority.ToArray();
			AdditionalRecords = additional.ToArray();
		}

        public override string ToString()
        {
            var buffer = new StringBuilder();
            buffer.Append("Packet {\n");
            buffer.Append("  Id=" + Id + "\n");
            buffer.Append("  IsQuery=" + IsQuery + "\n");
            buffer.Append("  QueryType=" + QueryType + "\n");
            buffer.Append("  IsAuthority=" + IsAuthority + "\n");
            buffer.Append("  WasTruncated=" + IsTruncated + "\n");
            buffer.Append("  RecursiveRequest=" + RecursiveRequest + "\n");
            buffer.Append("  RecursiveResponse=" + RecursiveResponse + "\n");
            buffer.Append("  ResponseType=" + ResponseType + "\n");

            foreach (var question in Questions)
            {
                buffer.Append("Question {\n");
                buffer.Append(question.ToString());
                buffer.Append("\n}\n");
            }

            foreach (var answer in Answers)
            {
                buffer.Append("Answer {\n");
                buffer.Append(answer.ToString());
                buffer.Append("\n}\n");
            }

            foreach (var authority in AuthoritativeAnswers)
            {
                buffer.Append("Authority {\n");
                buffer.Append(authority.ToString());
                buffer.Append("\n}\n");
            }

            foreach (var additional in AdditionalRecords)
            {
                buffer.Append("Additional {\n");
                buffer.Append(additional.ToString());
                buffer.Append("\n}\n");
            }

            buffer.Append("}");
            return buffer.ToString();
        }

        public override bool Equals(object other)
        {
            if (!(other is DNSPacket))
            {
                return false;
            }

            return Equals((DNSPacket)other);
        }

        public override int GetHashCode()
        {
            // See SO:
            // http://stackoverflow.com/questions/263400/what-is-the-best-algorithm-for-an-overridden-system-object-gethashcode
            unchecked
            {
                int hash = 17;
                hash = hash * 29 + (int)Id;
                hash = hash * 29 + (IsQuery ? 1 : 0);
                hash = hash * 29 + (int)QueryType;
                hash = hash * 29 + (IsAuthority ? 1 : 0);
                hash = hash * 29 + (IsTruncated ? 1 : 0);
                hash = hash * 29 + (RecursiveRequest ? 1 : 0);
                hash = hash * 29 + (RecursiveResponse ? 1 : 0);
                hash = hash * 29 + (int)ResponseType;

                foreach (var question in Questions)
                {
                    hash = hash * 29 + question.GetHashCode();
                }

                foreach (var answer in Answers)
                {
                    hash = hash * 29 + answer.GetHashCode();
                }

                foreach (var authority in AuthoritativeAnswers)
                {
                    hash = hash * 29 + authority.GetHashCode();
                }

                foreach (var additional in AdditionalRecords)
                {
                    hash = hash * 29 + additional.GetHashCode();
                }

                return hash;
            }
        }

        public bool Equals(DNSPacket other)
        {
            if (this.Questions.Length != other.Questions.Length)
            {
                return false;
            }

            if (this.Answers.Length != other.Answers.Length)
            {
                return false;
            }

            if (this.AuthoritativeAnswers.Length !=
                other.AuthoritativeAnswers.Length)
            {
                return false;
            }

            if (this.AdditionalRecords.Length !=
                other.AdditionalRecords.Length)
            {
                return false;
            }

            for (int i = 0; i < this.Questions.Length; i++)
            {
                if (!this.Questions[i].Equals(other.Questions[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < this.Answers.Length; i++)
            {
                if (!this.Answers[i].Equals(other.Answers[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < this.AuthoritativeAnswers.Length; i++)
            {
                if (!this.AuthoritativeAnswers[i].Equals(other.AuthoritativeAnswers[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < this.AdditionalRecords.Length; i++)
            {
                if (!this.AdditionalRecords[i].Equals(other.AdditionalRecords[i]))
                {
                    return false;
                }
            }

            return this.Id == other.Id &&
            this.IsQuery == other.IsQuery &&
            this.QueryType == other.QueryType &&
            this.IsAuthority == other.IsAuthority &&
            this.IsTruncated == other.IsTruncated &&
            this.RecursiveRequest == other.RecursiveRequest &&
            this.RecursiveResponse == other.RecursiveResponse &&
            this.ResponseType == other.ResponseType;
        }

        public void Serialize(DNSOutputStream stream)
        {
            stream.WriteUInt16(Id);

            var fields = new FieldGroup()
                .Add(new Field(1, IsQuery ? (byte)0 : (byte)1))
                .Add(new Field(4, (byte)QueryType))
                .Add(new Field(1, IsAuthority ? (byte)1 : (byte)0))
                .Add(new Field(1, IsTruncated ? (byte)1 : (byte)0))
                .Add(new Field(1, RecursiveRequest ? (byte)1 : (byte)0));
            stream.WriteByte(fields.Pack());

            fields = new FieldGroup()
                .Add(new Field(1, RecursiveResponse ? (byte)1 : (byte)0))
                .Add(new Field(3, 0))
                .Add(new Field(4, (byte)ResponseType));
            stream.WriteByte(fields.Pack());

            stream.WriteUInt16((UInt16)Questions.Length);
            stream.WriteUInt16((UInt16)Answers.Length);
            stream.WriteUInt16((UInt16)AuthoritativeAnswers.Length);
            stream.WriteUInt16((UInt16)AdditionalRecords.Length);

            foreach (var question in Questions)
            {
                question.Serialize(stream);
            }

            foreach (var answer in Answers)
            {
                answer.Serialize(stream);
            }

            foreach (var authority in AuthoritativeAnswers)
            {
                authority.Serialize(stream);
            }

            foreach (var additional in AdditionalRecords)
            {
                additional.Serialize(stream);
            }
        }

        public static DNSPacket Unserialize(DNSInputStream stream)
        {
            var id = stream.ReadUInt16();

            var is_query_bit = new Field(1);
            var query_type_flag = new Field(4);
            var is_authority_bit = new Field(1);
            var is_truncated_bit= new Field(1);
            var recursive_request_bit = new Field(1);
            new FieldGroup()
                .Add(is_query_bit)
                .Add(query_type_flag)
                .Add(is_authority_bit)
                .Add(is_truncated_bit)
                .Add(recursive_request_bit)
                .Unpack(stream.ReadByte());

            var is_query = is_query_bit.Value == 0;
            var query_type = (QueryType)query_type_flag.Value;
            var is_authority = is_authority_bit.Value == 1;
            var is_truncated = is_truncated_bit.Value == 1;
            var recursive_request = recursive_request_bit.Value == 1;

            var recursion_availble_bit = new Field(1);
            var zeroes = new Field(3);
            var response_type_flag = new Field(4);
            new FieldGroup()
                .Add(recursion_availble_bit)
                .Add(zeroes)
                .Add(response_type_flag)
                .Unpack(stream.ReadByte());

			var recursive_response = recursion_availble_bit.Value == 1;
            var response_type = (ResponseType)response_type_flag.Value;

            var question_count = stream.ReadUInt16();
            var answer_count = stream.ReadUInt16();
            var authority_count = stream.ReadUInt16();
            var additional_count = stream.ReadUInt16();

            var questions = new DNSQuestion[question_count];
            for (int i = 0; i < question_count; i++)
            {
                questions[i] = DNSQuestion.Unserialize(stream);
                if (questions[i] == null)
                {
                    throw new InvalidDataException("null Question " + i);
                }
            }

            var answers = new DNSRecord[answer_count];
            for (int i = 0; i < answer_count; i++)
            {
                answers[i] = DNSRecord.Unserialize(stream);
                if (answers[i] == null)
                {
                    throw new InvalidDataException("null Answer " + i);
                }
            }

            var authority = new DNSRecord[authority_count];
            for (int i = 0; i < authority_count; i++)
            {
                authority[i] = DNSRecord.Unserialize(stream);
                if (authority[i] == null)
                {
                    throw new InvalidDataException("null Authority " + i);
                }
            }

            var additional = new DNSRecord[additional_count];
            for (int i = 0; i < additional_count; i++)
            {
                additional[i] = DNSRecord.Unserialize(stream);
                if (additional[i] == null)
                {
                    throw new InvalidDataException("null Additional " + i);
                }
            }

			return new DNSPacket(id, is_query, query_type.Normalize(), is_authority, is_truncated, 
			                     recursive_request, recursive_response, response_type.Normalize(),
			                     questions, answers, authority, additional);
        }

        /**
         * A convenience wrapper around Serialize.
         */
        public byte[] ToBytes()
        {
            var buffer_stream = new MemoryStream();
            var compress = new CompressionOutputContext();
            var dns_out = new DNSOutputStream(buffer_stream, compress);
            Serialize(dns_out);
            return buffer_stream.ToArray();
        }

        /**
         * A convenience wrapper around Unserialize.
         */
        public static DNSPacket FromBytes(byte[] bytes)
        {
            var buffer_stream = new MemoryStream(bytes);
            var compress = new CompressionInputContext(bytes);
            var dns_in = new DNSInputStream(buffer_stream, compress);
            return Unserialize(dns_in);
        }
    }
}

