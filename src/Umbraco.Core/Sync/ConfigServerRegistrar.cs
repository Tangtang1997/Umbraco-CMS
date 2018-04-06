﻿using System.Collections.Generic;
using System.Linq;
using System.Web;
using Umbraco.Core.Configuration;
using Umbraco.Core.Configuration.UmbracoSettings;
using Umbraco.Core.IO;
using Umbraco.Core.Logging;

namespace Umbraco.Core.Sync
{
    /// <summary>
    /// Provides server registrations to the distributed cache by reading the legacy Xml configuration
    /// in umbracoSettings to get the list of (manually) configured server nodes.
    /// </summary>
    internal class ConfigServerRegistrar : IServerRegistrar
    {
        private readonly List<IServerAddress> _addresses;
        private readonly ServerRole _serverRole;
        private readonly string _umbracoApplicationUrl;

        public ConfigServerRegistrar(IUmbracoSettingsSection settings, ILogger logger, IGlobalSettings globalSettings)
            : this(settings.DistributedCall, logger, globalSettings)
        { }

        // for tests
        internal ConfigServerRegistrar(IDistributedCallSection settings, ILogger logger, IGlobalSettings globalSettings)
        {
            if (settings.Enabled == false)
            {
                _addresses = new List<IServerAddress>();
                _serverRole = ServerRole.Single;
                _umbracoApplicationUrl = null; // unspecified
                return;
            }

            var serversA = settings.Servers.ToArray();

            _addresses = serversA
                .Select(x => new ConfigServerAddress(x, globalSettings))
                .Cast<IServerAddress>()
                .ToList();

            if (serversA.Length == 0)
            {
                _serverRole = ServerRole.Unknown; // config error, actually
                logger.Debug<ConfigServerRegistrar>("Server Role Unknown: DistributedCalls are enabled but no servers are listed.");
            }
            else
            {
                var master = serversA[0]; // first one is master
                var appId = master.AppId;
                var serverName = master.ServerName;

                if (appId.IsNullOrWhiteSpace() && serverName.IsNullOrWhiteSpace())
                {
                    _serverRole = ServerRole.Unknown; // config error, actually
                    logger.Debug<ConfigServerRegistrar>("Server Role Unknown: Server Name or AppId missing from Server configuration in DistributedCalls settings.");
                }
                else
                {
                    _serverRole = IsCurrentServer(appId, serverName)
                        ? ServerRole.Master
                        : ServerRole.Slave;
                }
            }

            var currentServer = serversA.FirstOrDefault(x => IsCurrentServer(x.AppId, x.ServerName));
            if (currentServer != null)
            {
                // match, use the configured url
                // ReSharper disable once UseStringInterpolation
                _umbracoApplicationUrl = string.Format("{0}://{1}:{2}/{3}",
                    currentServer.ForceProtocol.IsNullOrWhiteSpace() ? "http" : currentServer.ForceProtocol,
                    currentServer.ServerAddress,
                    currentServer.ForcePortnumber.IsNullOrWhiteSpace() ? "80" : currentServer.ForcePortnumber,
                    IOHelper.ResolveUrl(SystemDirectories.Umbraco).TrimStart('/'));
            }
        }

        private static bool IsCurrentServer(string appId, string serverName)
        {
            // match by appId or computer name
            return (appId.IsNullOrWhiteSpace() == false && appId.Trim().InvariantEquals(HttpRuntime.AppDomainAppId))
                || (serverName.IsNullOrWhiteSpace() == false && serverName.Trim().InvariantEquals(NetworkHelper.MachineName));
        }

        public IEnumerable<IServerAddress> Registrations => _addresses;

        public ServerRole GetCurrentServerRole() => _serverRole;

        public string GetCurrentServerUmbracoApplicationUrl() => _umbracoApplicationUrl;
    }
}
