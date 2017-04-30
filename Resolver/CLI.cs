using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

using DNSProtocol;

namespace DNSResolver
{
    public class MainClass
    {
        public static void Main(string[] args)
        {
            Console.Write("Enter nameservers: ");
            var raw_addresses = Console.ReadLine().Split(',');
            var addresses = new IPAddress[raw_addresses.Length];
            var endpoints = new EndPoint[raw_addresses.Length];
            for (var i = 0; i < raw_addresses.Length; i++)
            {
                addresses[i] = IPAddress.Parse(raw_addresses[i].Trim());
                endpoints[i] = new IPEndPoint(addresses[i], 53);
            }

            var cache = new NoopCache();
            IResolver resolver;
            if (args.Length == 0 || args[0] == "debug")
            {
                resolver = new StubResolver(cache);
            }
            else
            {
                Console.WriteLine("Resolver must be either 'full' or 'debug'");
                return;
            }

            Console.Write("Enter domain: ");
            var domain = Console.ReadLine().Trim();
            try
            {
                var results = resolver.Resolve(new Domain(domain), ResourceRecordType.HOST_ADDRESS, endpoints);
                Console.WriteLine("=== Success ===");
                Console.WriteLine("*** Answers");
                foreach (var record in results.answers)
                {
                    var a_record = (AResource)record.Resource;
                    Console.WriteLine(" - " + a_record.Address);
                }

                Console.WriteLine("*** Aliases");
                foreach (var record in results.aliases)
                {
                    var cname_record = (CNAMEResource)record.Resource;
                    Console.WriteLine(" - " + record.Name + " => " + cname_record.Alias);
                }

                Console.WriteLine("*** Nameservers");
                foreach (var record in results.referrals)
                {
                    var ns_record = (NSResource)record.Resource;
                    Console.WriteLine(" - " + record.Name + " => " + ns_record.Nameserver);
                }

                Console.WriteLine("*** Nameserver IPs");
                foreach (var record in results.referral_additional)
                {
                    var a_record = (AResource)record.Resource;
                    Console.WriteLine(" - " + record.Name + " => " + a_record.Address);
                }
            }
            catch (ResolverException error)
            {
                Console.WriteLine("=== Failure ===");
                Console.WriteLine(error.ToString());
            }
        }
    }
}
