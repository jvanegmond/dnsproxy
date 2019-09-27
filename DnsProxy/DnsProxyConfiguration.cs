using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DnsProxy
{
    public class DnsProxyConfiguration
    {
        public List<string> GoodDnsServers { get; } = new List<string>();

        public static DnsProxyConfiguration Load(string data)
        {
            return JsonConvert.DeserializeObject<DnsProxyConfiguration>(data);
        }

        public static string Save(DnsProxyConfiguration data)
        {
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }
    }
}
