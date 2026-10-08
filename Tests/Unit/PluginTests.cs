using System;
using Jellyfin.Plugin.MediaCccDe;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class PluginTests
    {
        [Fact]
        public void Plugin_has_unique_GUID()
        {
            var plugin = CreatePlugin();
            var id = plugin.Id;

            Assert.NotEqual(Guid.Empty, id);
            Assert.NotEqual(Guid.Parse("00000000-0000-0000-0000-000000000000"), id);
        }

        [Fact]
        public void Plugin_inherits_BasePlugin()
        {
            var plugin = CreatePlugin();

            Assert.IsAssignableFrom<BasePlugin<PluginConfiguration>>(plugin);
        }

        [Fact]
        public void Plugin_implements_IHasWebPages()
        {
            var plugin = CreatePlugin();

            Assert.IsAssignableFrom<IHasWebPages>(plugin);
        }

        [Fact]
        public void Plugin_Name_not_empty()
        {
            var plugin = CreatePlugin();
            var name = plugin.Name;

            Assert.False(string.IsNullOrEmpty(name));
            Assert.True(name.Length > 0);
        }

        [Fact]
        public void Plugin_Description_not_empty()
        {
            var plugin = CreatePlugin();
            var description = plugin.Description;

            Assert.False(string.IsNullOrEmpty(description));
            Assert.True(description.Length > 0);
        }

        [Fact]
        public void PluginConfiguration_inherits_BasePluginConfiguration()
        {
            var config = new PluginConfiguration();

            Assert.IsAssignableFrom<BasePluginConfiguration>(config);
        }

        [Fact]
        public void PluginConfiguration_has_WatchlistPath()
        {
            var config = new PluginConfiguration();
            var property = config.GetType().GetProperty("WatchlistPath");

            Assert.NotNull(property);
            Assert.Equal(typeof(string), property.PropertyType);
            Assert.NotNull(property.GetValue(config));
        }

        [Fact]
        public void PluginConfiguration_has_PreferredQuality()
        {
            var config = new PluginConfiguration();
            var property = config.GetType().GetProperty("PreferredQuality");

            Assert.NotNull(property);
            Assert.Equal(typeof(string), property.PropertyType);
            Assert.Equal("hd", property.GetValue(config));
        }

        [Fact]
        public void PluginConfiguration_has_PreferredAudioLanguages()
        {
            var config = new PluginConfiguration();
            var property = config.GetType().GetProperty("PreferredAudioLanguages");

            Assert.NotNull(property);
            Assert.Equal(typeof(System.Collections.Generic.List<string>), property.PropertyType);
        }

        [Fact]
        public void PluginConfiguration_has_PreferredSubtitleLanguages()
        {
            var config = new PluginConfiguration();
            var property = config.GetType().GetProperty("PreferredSubtitleLanguages");

            Assert.NotNull(property);
            Assert.Equal(typeof(System.Collections.Generic.List<string>), property.PropertyType);
        }

        [Fact]
        public void PluginConfiguration_has_SyncIntervalHours()
        {
            var config = new PluginConfiguration();
            var property = config.GetType().GetProperty("SyncIntervalHours");

            Assert.NotNull(property);
            Assert.Equal(typeof(int), property.PropertyType);
            Assert.Equal(6, property.GetValue(config));
        }

        private static Plugin CreatePlugin()
        {
            var appPathsMock = new Mock<IApplicationPaths>();
            appPathsMock.Setup(x => x.PluginConfigurationsPath).Returns("/tmp/plugins/config");
            appPathsMock.Setup(x => x.DataPath).Returns("/tmp/plugins/data");
            appPathsMock.Setup(x => x.PluginsPath).Returns("/tmp/plugins");

            var xmlSerializerMock = new Mock<IXmlSerializer>();

            var ctor = typeof(Plugin).GetConstructor(new Type[] {
                typeof(IApplicationPaths),
                typeof(IXmlSerializer)
            });

            Assert.NotNull(ctor);

            var plugin = ctor.Invoke(new object[] { appPathsMock.Object, xmlSerializerMock.Object }) as Plugin;
            Assert.NotNull(plugin);

            return plugin;
        }
    }
}
