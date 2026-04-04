using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.MediaCccDe
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths appPaths, IXmlSerializer xmlSerializer) 
            : base(appPaths, xmlSerializer) { }
        
        public override string Name => "MediaCCCDe";
        public override Guid Id => Guid.Parse("e225c91a-ef11-41ca-b913-6491f15c2992");
        public override string Description => "Integrates media.ccc.de conference recordings into Jellyfin";
        
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo 
                { 
                    Name = Name, 
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.html" 
                }
            };
        }
    }
}