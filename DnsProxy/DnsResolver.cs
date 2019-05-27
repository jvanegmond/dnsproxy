using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly List<DnsClient> _badDnsClients = new List<DnsClient>();
        private readonly List<DnsClient> _goodDnsClients = new List<DnsClient>();

        private readonly List<IPAddress> _badIpAddresses = new List<IPAddress>();

        public DnsResolver(ICollection<string> badDnsServers, ICollection<string> goodDnsServers, ICollection<string> badIpAddressResponses)
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
                _badIpAddresses.Add(IPAddress.Parse(badIpAddress));
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
                    _badIpAddresses.Any(badIpAddress =>
                        answer.IPAddress.Equals(badIpAddress))))
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
    }
}