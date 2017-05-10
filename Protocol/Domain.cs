using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DNSProtocol
{
    /**
     * Although domains are easy to represent as an array of string components,
     * there are enough useful operations to perform on them that having them
     * in their own class is useful.
     */
    public class Domain : IEnumerable<string>
    {
        private string[] segments;

        public Domain(string domain)
        {
            // Domains represented 'natively' include the trailing . to
            // indicate the root domain. Domains without it are assumed
            // to be provided by the user, and need to have it appended.
            if (!domain.EndsWith("."))
            {
                domain = domain + ".";
            }

            segments = domain.Split('.');
            Validate();
            NormalizeCase();
        }

        public Domain(IEnumerable<string> segments)
        {
            this.segments = segments.ToArray();
            Validate();
            NormalizeCase();
        }

        // Publicly accessible information about the number and contents of
        // our segments
        public int Length
        {
            get { return segments.Length; }
        }

        public string this[int i]
        {
            get { return segments[i]; }
        }

        // Equality-related methods
        public override bool Equals(object other)
        {
            if (!(other is Domain))
            {
                return false;
            }

            var other_domain = (Domain)other;
            if (this.Length != other_domain.Length)
            {
                return false;
            }

            for (var i = 0; i < Length; i++)
            {
                if (this[i] != other_domain[i])
                {
                    return false;
                }
            }

            return true;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                // See SO:
                // http://stackoverflow.com/questions/263400/what-is-the-best-algorithm-for-an-overridden-system-object-gethashcode
                int hash = 17;
                foreach (var segment in segments)
                {
                    hash = hash * 29 + segment.GetHashCode();
                }
                return hash;
            }
        }

        public static bool operator ==(Domain a, Domain b)
        {
            if (Object.ReferenceEquals(a, b))
            {
                return true;
            }

            if (Object.ReferenceEquals(a, null) || Object.ReferenceEquals(b, null))
            {
                return false;
            }

            return a.Equals(b);
        }

        public static bool operator !=(Domain a, Domain b)
        {
            return !(a == b);
        }

        public IEnumerator<string> GetEnumerator()
        {
            return ((IEnumerable<string>)segments).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public override string ToString()
        {
            return String.Join(".", segments);
        }

        /**
         * Returns a Domain with the first n components removed. This could
         * throw an InvalidDataException if the resulting Domain is invalid.
         */
        public Domain Slice(int offset)
        {
            return new Domain(segments.Skip(offset));
        }

        /**
         * Returns true if the given Domain is below the current domain (i.e 
         * they share a common suffix)
         */
        public bool IsSubdomain(Domain other)
        {
            var prefix_length = other.Length - this.Length;
            var other_tail = other.Slice(prefix_length);
            return other_tail == this;
        }

        /**
         * Ensures that the domain name consists of only ASCII characters, and
         * only in the appropriate lengths.
         */
        private void Validate()
        {
            // Due to the following rule, these are prohibited
            if (segments.Length == 0)
            {
                throw new InvalidDataException("Domains cannot have zero length");
            }

            // First, it must end with an empty segment, signifying the root
            // of the domain hierarchy
            if (segments[segments.Length - 1] != "")
            {
                throw new InvalidDataException("Domains must end in a 0-length segment");
            }

			var allowed_chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-";

            // Individual segments can only be 63 bytes long
            var total_size = 0;
			var current_segment = 0;
            foreach (var segment in segments)
            {
                if (segment.Length > 63)
                {
                    throw new InvalidDataException("Domains cannot have segments longer than 63 characters");
                }

				if (segment.Length < 1 && current_segment != segments.Length - 1)
				{
                    throw new InvalidDataException("Domain segments cannot be empty");
				}

                foreach (var character in segment)
                {
					if (!allowed_chars.Contains(character))
                    {
                        throw new InvalidDataException("Domain segments can only have letters, digits and hyphens");
                    }
                }

				if (segment.Length >= 1 && segment[segment.Length - 1] == '-')
				{
					throw new InvalidDataException("Domain segments cannot end with a hyphen");
				}

                total_size += segment.Length;
				current_segment++;
            }

            // The RFC says that the length of the whole domain includes length
            // bytes; there is one per segment
            total_size += segments.Length;

            if (total_size > 255)
            {
                throw new InvalidDataException("Domains cannot be longer than 255 bytes (including separators)");
            }
        }

        /**
         * This converts the domain into lowercase, so that way we can avoid
         * doing the conversion every time we want to compare two domains
         * (especially important for GetHashCode - we can do case-insensitive
         * string comparisons, but not case-insensitive hashes)
         */
        private void NormalizeCase()
        {
            var lower_segments = new string[Length];
            for (var i = 0; i < Length; i++)
            {
                lower_segments[i] = this[i].ToLower();
            }

            segments = lower_segments;
        }
    }
}
