using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DNS.Client;
using DNS.Client.RequestResolver;
using DNS.Protocol;
using DNS.Protocol.ResourceRecords;
using NLog;

namespace DnsProxy
{
    public class DnsResolver : IRequestResolver
    {
        private class _DnsClient : DnsClient
        {
            public IPAddress IpAddress { get; }

            public _DnsClient(IPEndPoint dns) : base(dns)
            {
                IpAddress = dns.Address;
            }

            public _DnsClient(IPAddress ip, int port = 53) : base(ip, port)
            {
                IpAddress = ip;
            }

            public _DnsClient(string ip, int port = 53) : base(ip, port)
            {
                IpAddress = IPAddress.Parse(ip);
            }

            public _DnsClient(IRequestResolver resolver) : base(resolver)
            {
                IpAddress = null;
            }
        }

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly List<_DnsClient> _badDnsClients = new List<_DnsClient>();
        private readonly List<IPAddress> _badIpAddresses = new List<IPAddress>();
        private readonly List<_DnsClient> _goodDnsClients = new List<_DnsClient>();

        public DnsResolver(ICollection<string> goodDnsServers)
        {
            if (goodDnsServers.Count == 0) throw new ArgumentException("At least one good DNS server must be configured", nameof(goodDnsServers));

            foreach (var goodDnsServer in goodDnsServers)
            {
                _logger.Info($"Good DNS server: {goodDnsServer}");
                _goodDnsClients.Add(new _DnsClient(goodDnsServer));
            }
        }

        public void AddBadDnsServer(IPAddress ipAddress)
        {
            if (_badDnsClients.Any(_ => IpAddressEquals(_.IpAddress, ipAddress))) return;
            _badDnsClients.Add(new _DnsClient(ipAddress));
        }

        public void AddBadResolvedAddress(IPAddress ipAddress)
        {
            if (_badIpAddresses.Any(_ => IpAddressEquals(_, ipAddress))) return;
            _badIpAddresses.Add(ipAddress);
        }

        public async Task<IResponse> Resolve(IRequest request)
        {
            // Try all bad DNS clients till one returns an answer
            bool isBadResolver = true;

            IResponse result = null;
            foreach (var badDnsClient in _badDnsClients)
            {
                var resolver = badDnsClient.Create(request).Resolve();

                result = await resolver;
                if (result.AnswerRecords.Count != 0 || result.AuthorityRecords.Count != 0) break;
            }

            if (result == null ||
                result.AnswerRecords.OfType<IPAddressResourceRecord>().Any(answer =>
                    _badIpAddresses.Any(badIpAddress => IpAddressEquals(answer.IPAddress, badIpAddress))))
            {
                isBadResolver = false;
                foreach (var goodDnsClient in _goodDnsClients)
                {
                    var resolver = goodDnsClient.Create(request).Resolve();

                    result = await resolver;
                    if (result.AnswerRecords.Count != 0 || result.AuthorityRecords.Count != 0) break;
                }
            }

            if (result == null)
            {
                throw new Exception("No DNS clients were configured and this should not have happened");
            }

            foreach (var answer in result.AnswerRecords)
            {
                _logger.Log(LogLevel.Debug, $"{(isBadResolver ? "Bad: " : "Good: ")} {answer}");
            }

            return result;
        }

        private bool IpAddressEquals(IPAddress a, IPAddress b)
        {
            if (a.Equals(b)) return true;

            if (a.MapToIPv4().Equals(b.MapToIPv4())) return true;

            if (a.MapToIPv6().Equals(b.MapToIPv6())) return true;

            return false;
        }

        public static List<IPAddress> ResolveAddress(ICollection<string> badDnsServers, string badDomainName)
        {
            var result = new List<IPAddress>();

            // Do a few tries to get ALL possible IPv4 and IPv6 addresses
            for (var n = 0; n < 10; n++)
            {
                foreach (var badDnsServer in badDnsServers)
                {
                    try
                    {
                        var dnsClient = new DnsClient(badDnsServer);

                        var task = dnsClient.Resolve(badDomainName, RecordType.A);
                        Task.WaitAll(new Task[] {task}, TimeSpan.FromSeconds(2000));

                        result.AddRange(task.Result.AnswerRecords.OfType<IPAddressResourceRecord>().Select(_ => _.IPAddress));

                        task = dnsClient.Resolve(badDomainName, RecordType.AAAA);
                        Task.WaitAll(new Task[] {task}, TimeSpan.FromSeconds(2000));

                        result.AddRange(task.Result.AnswerRecords.OfType<IPAddressResourceRecord>().Select(_ => _.IPAddress));
                    }
                    catch (Exception ex)
                    {
                        _logger.Info(ex);
                    }
                }
            }

            return result.Distinct().ToList();
        }
    }
}