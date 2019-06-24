using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using NLog;

namespace DnsProxy
{
    public static class NetworkDnsSettingsHelper
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public static string[] GetNameServers()
        {
            // Set all adapters to manual configuration and return previously configured DNS services for all adapters

            List<string> dnsServers = new List<string>();

            using (var networkAdapterConfiguration = new ManagementClass("Win32_NetworkAdapterConfiguration"))
            {
                using (var networkAdapters = networkAdapterConfiguration.GetInstances())
                {
                    foreach (var networkAdapter in networkAdapters.Cast<ManagementObject>().Where(networkAdapter =>
                        (bool)networkAdapter["IPEnabled"] && networkAdapter["DNSDomain"] != null
                                                          && networkAdapter["DNSDomain"].ToString().EndsWith("nice.com")))
                    {
                        _logger.Info($"Get DNS servers on adapter {networkAdapter["Description"]}");

                        var existingDnsServers = (string[])networkAdapter["DNSServerSearchOrder"];

                        _logger.Info($"For adapter {networkAdapter["Description"]} got DNS servers: {string.Join(", ", existingDnsServers)}");

                        dnsServers.AddRange(existingDnsServers);
                    }
                }
            }

            return dnsServers.Distinct().ToArray();
        }

        public static void ConfigureNameServers(string[] newDnsServers)
        {
            // Set all adapters to manual configuration

            using (var networkAdapterConfiguration = new ManagementClass("Win32_NetworkAdapterConfiguration"))
            {
                using (var networkAdapters = networkAdapterConfiguration.GetInstances())
                {
                    foreach (var networkAdapter in networkAdapters.Cast<ManagementObject>().Where(networkAdapter =>
                        (bool)networkAdapter["IPEnabled"] && networkAdapter["DNSDomain"] != null
                                                          && networkAdapter["DNSDomain"].ToString().EndsWith("nice.com")))
                    {
                        _logger.Info($"Set DNS servers on adapter {networkAdapter["Description"]}");

                        using (var setDnsServerSearchOrderMethodParameters = networkAdapter.GetMethodParameters("SetDNSServerSearchOrder"))
                        {
                            setDnsServerSearchOrderMethodParameters["DNSServerSearchOrder"] = newDnsServers;
                            var result = networkAdapter.InvokeMethod("SetDNSServerSearchOrder", setDnsServerSearchOrderMethodParameters, null);
                            var errorCode = (uint)result["ReturnValue"];
                            if (errorCode != 0)
                            {
                                throw new Exception("Unable to set DNS server with error number (see SetDNSServerSearchOrder error codes): " + errorCode);
                            }
                        }
                    }
                }
            }
        }

        public static void ConfigureNameServersAutomatic()
        {
            using (var networkAdapterConfiguration = new ManagementClass("Win32_NetworkAdapterConfiguration"))
            {
                using (var networkAdapters = networkAdapterConfiguration.GetInstances())
                {
                    foreach (var networkAdapter in networkAdapters.Cast<ManagementObject>().Where(networkAdapter =>
                        (bool)networkAdapter["IPEnabled"] && networkAdapter["DNSDomain"] != null
                                                          && networkAdapter["DNSDomain"].ToString().EndsWith("nice.com")))
                    {
                        _logger.Info($"Set DNS servers on adapter {networkAdapter["Description"]} to automatic");
                        try
                        {
                            using (var setDnsServerSearchOrderMethodParameters = networkAdapter.GetMethodParameters("SetDNSServerSearchOrder"))
                            {
                                var result = networkAdapter.InvokeMethod("SetDNSServerSearchOrder", setDnsServerSearchOrderMethodParameters, null);
                                var errorCode = (uint)result["ReturnValue"];
                                if (errorCode != 0)
                                {
                                    throw new Exception("Unable to set DNS server with error number (see SetDNSServerSearchOrder error codes): " + errorCode);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error("An error occurred while resetting DNS servers to automatic for adapter {0}", networkAdapter["Description"]);
                        }
                    }
                }
            }
        }
    }
}