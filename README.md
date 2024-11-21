DNS proxy which can send DNS requests to multiple servers, picking and forwarding the correct response based on a known bad response. 

DNS servers are always authorative on any DNS reply. If a DNS server does not know how to resolve an address, the DNS client assumes that this means that the answer is unknowable. This project implements a DNS server which can query multiple DNS servers simultaneously and use the most desirable reply. 

It runs locally as a service or on command line. After installation, configure the internet network adapter to use localhost as a DNS server.
