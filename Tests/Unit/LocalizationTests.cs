using System.Linq;
using Jellyfin.Plugin.MediaCccDe.Controllers;
using Jellyfin.Plugin.MediaCccDe.Services;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class LocalizationTests
    {
        [Fact]
        public void Catalogs_have_identical_complete_key_sets()
        {
            Assert.Equal(Translations.EnglishKeys.OrderBy(key => key), Translations.GermanKeys.OrderBy(key => key));
        }

        [Fact]
        public void Catalog_values_are_nonempty_and_not_key_placeholders()
        {
            foreach (var key in Translations.EnglishKeys)
            {
                Assert.False(string.IsNullOrWhiteSpace(Translations.For("en")[key]));
                Assert.False(string.IsNullOrWhiteSpace(Translations.For("de")[key]));
                Assert.NotEqual(key, Translations.For("en")[key]);
                Assert.NotEqual(key, Translations.For("de")[key]);
            }
        }

        [Theory]
        [InlineData("en", "en")]
        [InlineData("de", "de")]
        [InlineData("de-DE", "de")]
        [InlineData("DE", "de")]
        [InlineData("de_AT", "de")]
        [InlineData("en-GB", "en")]
        public void Explicit_language_is_normalized(string value, string expected)
        {
            Assert.Equal(expected, PluginLanguage.Resolve(value, "de-DE,de;q=0.9"));
        }

        [Theory]
        [InlineData("fr")]
        [InlineData("xx")]
        [InlineData("garbage")]
        [InlineData("")]
        public void Unsupported_explicit_language_falls_back_to_english(string value)
        {
            Assert.Equal("en", PluginLanguage.Resolve(value, "de"));
        }

        [Fact]
        public void Header_is_used_when_no_explicit_language_is_provided()
        {
            Assert.Equal("de", PluginLanguage.Resolve(null, "de-AT,de;q=0.9,en;q=0.8"));
        }

        [Fact]
        public void Explicit_language_wins_over_header()
        {
            Assert.Equal("de", PluginLanguage.Resolve("de", "en-US,en;q=0.9"));
        }

        [Theory]
        [InlineData("de", "<html lang=\"de\">", "Media.CCC.de-Einstellungen")]
        [InlineData("en", "<html lang=\"en\">", "Media.CCC.de Settings")]
        public void Settings_renderer_uses_selected_language(string language, string expectedLang, string expectedTitle)
        {
            var html = SettingsPageHtml.Render(null, null, null, language, Translations.For(language));

            Assert.Contains(expectedLang, html);
            Assert.Contains(expectedTitle, html);
        }

        [Theory]
        [InlineData("de", "<html lang=\"de\">", "Dein")]
        [InlineData("en", "<html lang=\"en\">", "Your")]
        public void User_settings_renderer_uses_selected_language(string language, string expectedLang, string expectedTitle)
        {
            var html = UserSettingsPageHtml.Render(null, null, null, null, null, language, Translations.For(language));

            Assert.Contains(expectedLang, html);
            Assert.Contains(expectedTitle, html);
        }

        [Fact]
        public void Rendered_values_are_html_encoded()
        {
            var configuration = new PluginConfiguration { WatchlistPath = "<script>alert(1)</script>" };
            var html = SettingsPageHtml.Render(configuration, null, null, "en", Translations.For("en"));

            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;script&gt;", html);
        }
    }
}
