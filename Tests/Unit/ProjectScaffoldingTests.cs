using System.IO;
using System.Xml.Linq;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests
{
    public class ProjectScaffoldingTests
    {
        private static string GetProjectRoot()
        {
            var currentDir = Directory.GetCurrentDirectory();
            while (currentDir != null && !File.Exists(Path.Combine(currentDir, "Jellyfin.Plugin.MediaCccDe.csproj")))
            {
                currentDir = Directory.GetParent(currentDir)?.FullName;
            }
            return currentDir ?? Directory.GetCurrentDirectory();
        }

        private static string CsprojPath => Path.Combine(GetProjectRoot(), "Jellyfin.Plugin.MediaCccDe.csproj");
        private static string SolutionPath => Path.Combine(GetProjectRoot(), "Jellyfin.Plugin.MediaCccDe.sln");

        [Fact]
        public void csproj_targets_net10_0()
        {
            // Arrange
            Assert.True(File.Exists(CsprojPath), $"Project file {CsprojPath} should exist");

            // Act
            var csprojContent = File.ReadAllText(CsprojPath);
            var xdoc = XDocument.Parse(csprojContent);
            var project = xdoc.Element("Project");
            var propertyGroup = project?.Element("PropertyGroup");
            var targetFramework = propertyGroup?.Element("TargetFramework");

            // Assert
            Assert.NotNull(targetFramework);
            Assert.Equal("net10.0", targetFramework?.Value);
        }

        [Fact]
        public void Jellyfin_packages_exclude_runtime()
        {
            // Arrange
            Assert.True(File.Exists(CsprojPath), $"Project file {CsprojPath} should exist");

            // Act
            var csprojContent = File.ReadAllText(CsprojPath);
            var xdoc = XDocument.Parse(csprojContent);
            var project = xdoc.Element("Project");
            var itemGroups = project?.Elements("ItemGroup");

            var jellyfinPackages = itemGroups?
                .Elements("PackageReference")
                .Where(pr => pr.Attribute("Include")?.Value.StartsWith("Jellyfin.") == true)
                .ToList();

            // Assert
            Assert.NotNull(jellyfinPackages);
            Assert.NotEmpty(jellyfinPackages);

            foreach (var package in jellyfinPackages)
            {
                // Check for ExcludeAssets as either a child element or attribute
                var excludeAssetsElement = package.Element("ExcludeAssets");
                var excludeAssetsAttribute = package.Attribute("ExcludeAssets");

                var hasExcludeRuntime = (excludeAssetsElement != null &&
                    excludeAssetsElement.Value.IndexOf("runtime", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (excludeAssetsAttribute != null &&
                    excludeAssetsAttribute.Value.IndexOf("runtime", StringComparison.OrdinalIgnoreCase) >= 0);

                Assert.True(hasExcludeRuntime,
                    $"Jellyfin package '{package.Attribute("Include")?.Value}' should exclude runtime assets. " +
                    "Add <ExcludeAssets>runtime</ExcludeAssets> as a child element.");
            }
        }

        [Fact]
        public void Solution_file_exists()
        {
            // Assert
            Assert.True(File.Exists(SolutionPath), $"Solution file {SolutionPath} should exist");
        }
    }
}
