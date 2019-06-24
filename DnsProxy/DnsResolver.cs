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
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly List<DnsClient> _badDnsClients = new List<DnsClient>();
        private readonly List<DnsClient> _goodDnsClients = new List<DnsClient>();

        private readonly List<IPAddress> _badIpAddresses = new List<IPAddress>();

        public DnsResolver(ICollection<string> badDnsServers, ICollection<string> goodDnsServers, ICollection<IPAddress> badIpAddressResponses)
        {
            if (badDnsServers.Count == 0) throw new ArgumentException("At least one bad DNS server must be configured", nameof(badDnsServers));
            if (goodDnsServers.Count == 0) throw new ArgumentException("At least one good DNS server must be configured", nameof(goodDnsServers));

            foreach (var badDnsServer in badDnsServers)
            {
                _logger.Info($"Bad DNS server: {badDnsServer}");
                _badDnsClients.Add(new DnsClient(badDnsServer));
            }

            foreach (var goodDnsServer in goodDnsServers)
            {
                _logger.Info($"Good DNS server: {goodDnsServer}");
                _goodDnsClients.Add(new DnsClient(goodDnsServer));
            }

            foreach (var badIpAddress in badIpAddressResponses)
            {
                _logger.Info($"Bad IP address: {badIpAddress}");
                _badIpAddresses.Add(badIpAddress);
            }
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

        public static List<IPAddress> ResolveAddress(string[] badDnsServers, string badDomainName)
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