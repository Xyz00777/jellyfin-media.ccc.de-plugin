using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MediaCccDe.Controllers;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class ControllerDependencyRegistrationTests
    {
        private static readonly Type[] PluginControllers =
        {
            typeof(MediaCccController),
            typeof(SyncController),
            typeof(DownloadController)
        };

        public static IEnumerable<object[]> Controllers =>
            PluginControllers.Select(c => new object[] { c });

        /// <summary>
        /// Dependencies the Jellyfin host resolves for us. Anything the plugin itself
        /// provides must be registered by ServiceRegistrator, otherwise activating the
        /// controller throws at request time with HTTP 500.
        /// </summary>
        private static bool IsHostProvided(Type type)
        {
            var ns = type.Namespace ?? string.Empty;
            return ns.StartsWith("Microsoft.", StringComparison.Ordinal)
                || ns.StartsWith("MediaBrowser.", StringComparison.Ordinal)
                || ns.StartsWith("System.", StringComparison.Ordinal);
        }

        [Theory]
        [MemberData(nameof(Controllers))]
        public void Every_controller_dependency_is_registered(Type controllerType)
        {
            var serviceCollection = new ServiceCollection();
            var registrator = new ServiceRegistrator();
            registrator.RegisterServices(serviceCollection, new Mock<IServerApplicationHost>().Object);

            var registered = serviceCollection.Select(d => d.ServiceType).ToHashSet();

            var constructor = controllerType.GetConstructors().Single();
            var missing = constructor.GetParameters()
                .Select(p => p.ParameterType)
                .Where(t => !t.ContainsGenericParameters)
                .Where(t => t != typeof(System.Func<PluginConfiguration>))
                .Where(t => !IsHostProvided(t))
                .Where(t => !registered.Contains(t))
                .Select(t => t.FullName)
                .ToList();

            Assert.True(
                missing.Count == 0,
                $"{controllerType.Name} has unregistered dependencies: {string.Join(", ", missing)}");
        }
    }
}
