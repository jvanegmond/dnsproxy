using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using DNS.Client;
using DNS.Server;
using Microsoft.SqlServer.Server;
using NLog;

namespace DnsProxy
{
    public class DnsProxyService : IDisposable
    {

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly DnsServer _server;
        private readonly Thread _configurationThread;
        private bool _stop;
        private readonly ManualResetEvent _configurationThreadEnded = new ManualResetEvent(false);

        private List<NetworkInterface> _managedNetworkInterfaces = new List<NetworkInterface>();
        private readonly DnsResolver _resolver;

        public DnsProxyService()
        {
            _logger.Info("Creating DNS proxy service");

            DnsProxyConfiguration config;
            try
            {
                config = GetConfig();
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                // Set up DNS server
                _resolver = new DnsResolver(config.GoodDnsServers);
                _server = new DnsServer(_resolver);

                var endPoint = new IPEndPoint(IPAddress.Any, 53);

                _logger.Info($"Starting listening on {endPoint}");

                _server.Listen(endPoint);

                // Set up all network interfaces
                _managedNetworkInterfaces.AddRange(NetworkDnsSettingsHelper.GetNetworkInterfaces());

                foreach (var managedNetworkInterface in _managedNetworkInterfaces)
                {
                    SetupNetworkAdapter(managedNetworkInterface);
                }

                FlushDns();
            }
            catch (Exception err)
            {
                _logger.Error($"Error starting service: {err}");
            }

            _configurationThread = new Thread(ConfigurationThread);
            _configurationThread.Start();
        }

        private void ConfigurationThread()
        {
            while (!_stop)
            {
                try
                {
                    var previousState = _managedNetworkInterfaces;

                    var currentState = NetworkDnsSettingsHelper.GetNetworkInterfaces();

                    // Removed network interfaces can be set to automatic again
                    foreach (var managedNetworkInterface in previousState.Except(currentState))
                    {
                        NetworkDnsSettingsHelper.ConfigureNameServersAutomatic(managedNetworkInterface);
                    }

                    // Added network interfaces should be set to manual
                    foreach (var managedNetworkInterface in currentState.Except(previousState))
                    {
                        SetupNetworkAdapter(managedNetworkInterface);
                    }

                    _managedNetworkInterfaces = currentState;

                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }
                catch (Exception err)
                {
                    _logger.Error(err, "An unexpected error occurred: ");
                }
            }

            _configurationThreadEnded.Set();
        }

        private void SetupNetworkAdapter(NetworkInterface managedNetworkInterface)
        {
            NetworkDnsSettingsHelper.ConfigureNameServersAutomatic(managedNetworkInterface);

            var badDnsServers = NetworkDnsSettingsHelper.GetNameServers(managedNetworkInterface);

            _logger.Info($"From adapter {managedNetworkInterface.Description} got bad DNS servers {string.Join(", ", badDnsServers)}");
            foreach (var badDnsServer in badDnsServers)
            {
                _resolver.AddBadDnsServer(IPAddress.Parse(badDnsServer));
            }

            var badIpAddresses = DnsResolver.ResolveAddress(badDnsServers, "fastmail.com"); // Any blocked domain here will do

            _logger.Info($"From adapter {managedNetworkInterface.Description} got bad IP addresses {string.Join(", ", badIpAddresses)}");
            foreach (var badIpAddress in badIpAddresses)
            {
                _resolver.AddBadResolvedAddress(badIpAddress);
            }

            NetworkDnsSettingsHelper.ConfigureNameServers(managedNetworkInterface, new[] { "127.0.0.1" });
        }

        private static void FlushDns()
        {
            var startInfo = new ProcessStartInfo("ipconfig", "/flushdns");
            startInfo.RedirectStandardOutput = true;
            startInfo.UseShellExecute = false;
            var process = Process.Start(startInfo);
            _logger.Info("FlushDNS result: \r\n" + process.StandardOutput.ReadToEnd());
        }

        private DnsProxyConfiguration GetConfig()
        {
            const string configFile = "config.json";
            string configuration = null;

#if !DEBUG
            try
            {
                var uri = new Uri("http://spaces.cssrd.local/jvanegmond/" + configFile);
                _logger.Info("Downloading configuration from " + uri);
                configuration = new WebClient().DownloadString(uri);
                File.WriteAllText(configFile, configuration, Encoding.UTF8);
                _logger.Info("Downloaded new configuration");
            }
            catch (Exception ex1)
            {
                _logger.Error(ex1);
            }

            if (configuration == null)
#endif
            {
                try
                {
                    // Use cached config
                    configuration = File.ReadAllText(configFile, Encoding.UTF8);
                }
                catch (Exception ex2)
                {
                    _logger.Error(ex2);
                    return null;
                }
            }

            var config = DnsProxyConfiguration.Load(configuration);

            if (config == null)
            {
                _logger.Error("Config is null");
            }
            else
            {
                _logger.Debug($"Got config. Good DNS servers: {string.Join(", ", config.GoodDnsServers)}.");
            }

            return config;
        }

        public void Dispose()
        {
            _logger.Info("Stopping service");

            foreach (var networkInterface in _managedNetworkInterfaces)
            {
                NetworkDnsSettingsHelper.ConfigureNameServersAutomatic(networkInterface);
            }

            _stop = true;
            _configurationThread.Abort();

            _server?.Dispose();
        }
    }
}
