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

        public static string[] ConfigureNameServers(string[] newDnsServers)
        {
            // Reset all adapters to automatic configuration

            using (var networkAdapterConfiguration = new ManagementClass("Win32_NetworkAdapterConfiguration"))
            {
                using (var networkAdapters = networkAdapterConfiguration.GetInstances())
                {
                    foreach (var networkAdapter in networkAdapters.Cast<ManagementObject>().Where(networkAdapter =>
                        (bool)networkAdapter["IPEnabled"] && networkAdapter["DNSDomain"] != null && networkAdapter["DNSDomain"].Equals("eu.nice.com")))
                    {
                        var existingDnsServers = (string[])networkAdapter["DNSServerSearchOrder"];

                        // If we are trying to apply the old configuration, we have to first set the NIC to automatic and wait for DNS servers to be configured
                        if (existingDnsServers.SequenceEqual(newDnsServers))
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
                    }
                }
            }

            // Set all adapters to manual configuration and return aggregated DNS services for all adapters

            List<string> dnsServers = new List<string>();

            using (var networkAdapterConfiguration = new ManagementClass("Win32_NetworkAdapterConfiguration"))
            {
                using (var networkAdapters = networkAdapterConfiguration.GetInstances())
                {
                    foreach (var networkAdapter in networkAdapters.Cast<ManagementObject>().Where(networkAdapter =>
                        (bool)networkAdapter["IPEnabled"] && networkAdapter["DNSDomain"] != null && networkAdapter["DNSDomain"].Equals("eu.nice.com")))
                    {
                        _logger.Info($"Set DNS server on adapter {networkAdapter["Description"]}");

                        var existingDnsServers = (string[])networkAdapter["DNSServerSearchOrder"];

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

                        dnsServers.AddRange(existingDnsServers);
                    }
                }
            }

            return dnsServers.Distinct().ToArray();
        }

        public static void ResetNameServers(string networkAdapterDescription)
        {
            using (var networkAdapterConfiguration = new ManagementClass("Win32_NetworkAdapterConfiguration"))
            {
                using (var networkAdapters = networkAdapterConfiguration.GetInstances())
                {
                    foreach (var networkAdapter in networkAdapters.Cast<ManagementObject>().Where(networkAdapter => (bool)networkAdapter["IPEnabled"] && networkAdapter["Description"].Equals(networkAdapterDescription)))
                    {
                        var existingDnsServers = (string[])networkAdapter["DNSServerSearchOrder"];

                        // If we are trying to apply the old configuration, we have to first set the NIC to automatic and wait for DNS servers to be configured

                        using (var setDnsServerSearchOrderMethodParameters = networkAdapter.GetMethodParameters("SetDNSServerSearchOrder"))
                        {
                            var result = networkAdapter.InvokeMethod("SetDNSServerSearchOrder", setDnsServerSearchOrderMethodParameters, null);
                            var errorCode = (uint)result["ReturnValue"];
                            if (errorCode != 0)
                            {
                                throw new Exception("Unable to set DNS server with error number (see SetDNSServerSearchOrder error codes): " + errorCode);
                            }
                        }

                        return;
                    }
                }
            }

            throw new ArgumentException("Network adapter not found by description", nameof(networkAdapterDescription));
        }
    }
}