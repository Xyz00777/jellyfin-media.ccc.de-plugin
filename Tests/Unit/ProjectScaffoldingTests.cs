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
        public void csproj_targets_net9_0()
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
            Assert.Equal("net9.0", targetFramework?.Value);
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
            var itemGroup = project?.Element("ItemGroup");
            
            var jellyfinPackages = itemGroup?.Elements("PackageReference")
                .Where(pr => pr.Attribute("Include")?.Value.StartsWith("Jellyfin.") == true);
            
            // Assert
            Assert.NotNull(jellyfinPackages);
            Assert.NotEmpty(jellyfinPackages);
            
            foreach (var package in jellyfinPackages)
            {
                var excludeAssets = package.Element("ExcludeAssets");
                Assert.NotNull(excludeAssets);
                Assert.Equal("runtime", excludeAssets?.Value, ignoreCase: true);
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