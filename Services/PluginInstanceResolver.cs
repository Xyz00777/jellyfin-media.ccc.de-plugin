using System;
using MediaBrowser.Common.Plugins;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// The plugin instance is not registered in dependency injection, and resolving it
    /// from the application host returns null, so it is looked up through IPluginManager.
    /// </summary>
    public sealed class PluginInstanceResolver
    {
        private readonly IPluginManager _pluginManager;

        public PluginInstanceResolver(IPluginManager pluginManager)
        {
            _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        }

        public Plugin? Resolve()
        {
            return _pluginManager.GetPlugin(Plugin.PluginGuid, Plugin.PluginVersion)?.Instance as Plugin;
        }
    }
}
