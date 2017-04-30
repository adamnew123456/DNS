using System;
using System.Linq;
using NUnit.Framework;

namespace DNSProtocol
{
    [TestFixture]
    public class DomainTest
    {
        /**
         * Domain equality underlies most of the test suite, so we have to
         * check that both == and Equals work.
         */
        [Test]
        public void testDomainEquality()
        {
            var a = new Domain(new string[] { "example", "com", "" });
            var b = new Domain(new string[] { "example", "com", "" });

            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
        }

        // Domains should be parsable from both forms ending in ., and forms
        // not ending in .
        [Test]
        public void testParseDomain()
            {
                var expected = new Domain(new string[] { "example", "com", "" });
                var actual_root = new Domain("example.com.");
                Assert.That(actual_root, Is.EqualTo(expected));

                var actual_user = new Domain("example.com");
                Assert.That(actual_user, Is.EqualTo(expected));
            }

        // Check that the properties on the Domain are sane
        [Test]
        public void testDomainProperties()
        {
            var actual = new Domain("example.com");

            Assert.That(3, Is.EqualTo(actual.Length));
            Assert.That("example", Is.EqualTo(actual[0]));
            Assert.That("com", Is.EqualTo(actual[1]));
            Assert.That("", Is.EqualTo(actual[2]));
        }

        // Check that domains can be enumerated correctly
        [Test]
        public void testDomainEnumerator()
        {
            var actual = new Domain("example.com");
            var expected = new string[] { "example", "com", "" };

            Assert.That(actual.ToArray(), Is.EqualTo(expected));
        }

        // Check that the Domain->string conversion is sane
        [Test]
        public void testDomainToString()
        {
            var actual = new Domain("example.com");
            var expected = "example.com.";

            Assert.That(actual.ToString(), Is.EqualTo(expected));
        }

        // Check that Domain slicing gives the proper results
        [Test]
        public void testDomainSlice()
        {
            var actual = new Domain("foo.example.com");
            var expected = new Domain("example.com");

            Assert.That(actual.Slice(1), Is.EqualTo(expected));
        }

        // Check that subdomains are identified as such
        [Test]
        public void testSubdomain()
        {
            var base_domain = new Domain("example.com");
            var subdomain = new Domain("foo.example.com");

            Assert.IsTrue(base_domain.IsSubdomain(subdomain));
        }

        // Check that Domain lower-cases its input
        [Test]
        public void testDomainLowerCase()
        {
            var actual = new Domain("eXaMpLe.CoM");

            Assert.That(actual.Length, Is.EqualTo(3));
            Assert.That(actual[0], Is.EqualTo("example"));
            Assert.That(actual[1], Is.EqualTo("com"));
            Assert.That(actual[2], Is.EqualTo(""));
        }
    }
}
