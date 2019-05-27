using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DNS.Server;
using Microsoft.SqlServer.Server;
using NLog;

namespace DnsProxy
{
    public class DnsProxyService : IDisposable
    {
        private const string _networkAdapterDescription = "Intel(R) Ethernet Connection (5) I219-V";
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private DnsServer _server;

        public DnsProxyService()
        {
            _logger.Info("Creating DNS proxy service");

            DnsProxyConfiguration config;
            try
            {
                config = GetConfig();
            }
            catch (Exception ex)
            {
                return;
            }

            try
            {
                var badDnsServers = NetworkDnsSettingsHelper.ConfigureNameServers(new[] {"127.0.0.1"});

                var resolver = new DnsResolver(badDnsServers, config.GoodDnsServers, config.BadIpAddressResponses);
                _server = new DnsServer(resolver);

                var endPoint = new IPEndPoint(IPAddress.Any, 53);

                _logger.Info($"Starting listening on {endPoint}");

                _server.Listen(endPoint);
            }
            catch (Exception err)
            {
                _logger.Error($"Error starting service: {err}");
            }
        }

        private DnsProxyConfiguration GetConfig()
        {
            const string configFile = "config.json";
            string configuration;

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
                try
                {
                    _logger.Info("Getting configuration from local file");

                    // Use cached config
                    configuration = File.ReadAllText(configFile, Encoding.UTF8);

                    _logger.Info("Got configuration from local file");
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
                _logger.Info($"Got config. Good DNS servers: {string.Join(", ", config.GoodDnsServers)}. Bad IP addresses: {string.Join(", ", config.BadIpAddressResponses)}");
            }

            return config;
        }

        public void Dispose()
        {
            _logger.Info("Stopping service");

            try
            {
                NetworkDnsSettingsHelper.ResetNameServers(_networkAdapterDescription);
            }
            catch (Exception err)
            {
                _logger.Error($"Error resetting DNS servers: {err}");
            }

            _server.Dispose();
        }
    }
}
