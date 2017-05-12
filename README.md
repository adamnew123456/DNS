# DNS Server, Resolver and Protocol Library

**Note** This is mostly for exploration purposes - there are many
parts of DNS (many resource record types, administrative functions
like zone transfers and configuration, security features) that this
does not support.

This package provides a DNS protocol library, and a basic DNS resolver and
server which make use of it.

# Building

1. Regenerate the resource record classes by executing the T4 
   template `Protocol/ResourceRecordImpl.tt`. This may or may not be done 
   automatically by the next step, so its important do it now.
2. Build via `msbuild`/`xbuild` at the top level, which will compile the 
   protocol library, the resolver library, a basic command-line resolver, 
   and the server.

# Running The Tests

All tests are built as part of the normal build process - to execute them,
you can run `nunit3-console` on `Protocol/bin/Debug/Protocol.dll`,
`Resolver/bin/Debug/Resolver.dll` and `Server/bin/Debug/Server.exe`.

# Using the Resolver

The command-line resolver is mostly only useful for testing the resolution
process; it doesn't support nearly as many options as native nslookup does.
Currently, it will only fetch A records for a single domain.

You can run it by going to the `Resolver/bin/Debug` directory and executing
`Resolver.exe` it will prompt you for a list of nameserver IP addresses 
(separated by commas) and a hostname to resolve. It will then print out the
resulting response, as a list of answers (A records), aliases 
(CNAME records), authoritative nameservers, and addresses for those 
nameservers.

For example:

    $ Resolver.exe
    Enter nameservers: 8.8.8.8
    Enter domain: www.github.com
    === Success ===
    *** Answers
     - 192.30.253.113
     - 192.30.253.112
    *** Aliases
     - www.github.com. => github.com.
    *** Nameservers
    *** Nameserver IPs

# Using The Nameserver

First, you'll need a zone file to define what records the server has
authoritative knowledge over. Traditional master files are not yet supported;
currently, the zone is configured via an XML format:

    <!-- 
    Only one zone is allowed, and it is defined within the zone tag.
    -->
    <zone>
        <!-- 
        There must be exactly one SOA record - no more, and no fewer.
        -->
        <SOA name="lan" class="IN" ttl="86400"
             primary-ns="ns.lan" hostmaster="bob.lan"
             serial="0" refresh="3600" retry="60" expire="3600"
             min-ttl="3600" />

        <!-- 
        These are all of the supported resource records.
        -->

        <A name="bobs-computer.lan" class="IN" ttl="86400"
           address="192.168.0.1" />

        <AAAA name="bobs-computer.lan" class="IN" ttl="86400"
              address="2001:cafe:beef::" />

        <CNAME name="bob.lan" class="IN" ttl="86400"
               alias="bobs-computer.lan" />

        <NS name="lan" class="IN" ttl="86400"
            nameserver="other-ns.lan" />

        <MX name="lan" class="IN" ttl="86400"
            priority="1" mailserver="smtp.lan" />

        <PTR name="1.0.168.192.in-addr.arpa" class="IN" ttl="86400"
             pointer="bobs-computer.lan" />
    </zone>

Then, start up the server by giving the name of the zone file, the IP address
of the interface to serve requests over, and the port number.

    $ Server.exe zone.xml 127.0.0.1 53
