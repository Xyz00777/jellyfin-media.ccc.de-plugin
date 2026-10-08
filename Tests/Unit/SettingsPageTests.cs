using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Controllers;
using Jellyfin.Plugin.MediaCccDe.Services;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class SettingsPageTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "ccc-settings-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_path))
            {
                Directory.Delete(_path, true);
            }
        }

        #region Access token

        [Fact]
        public async Task Token_is_stable_across_calls()
        {
            var store = new SettingsAccessTokenStore(_path);

            var first = await store.GetAsync(CancellationToken.None);
            var second = await store.GetAsync(CancellationToken.None);

            Assert.Equal(first, second);
            Assert.Equal(64, first.Length);
        }

        [Fact]
        public async Task Token_survives_a_new_store_instance()
        {
            var first = await new SettingsAccessTokenStore(_path).GetAsync(CancellationToken.None);
            var second = await new SettingsAccessTokenStore(_path).GetAsync(CancellationToken.None);

            Assert.Equal(first, second);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("wrong", false)]
        public async Task Matches_rejects_a_bad_token(string? candidate, bool expected)
        {
            var store = new SettingsAccessTokenStore(_path);
            var token = await store.GetAsync(CancellationToken.None);

            Assert.Equal(expected, store.Matches(candidate, token));
        }

        [Fact]
        public async Task Matches_accepts_the_real_token()
        {
            var store = new SettingsAccessTokenStore(_path);
            var token = await store.GetAsync(CancellationToken.None);

            Assert.True(store.Matches(token, token));
        }

        [Fact]
        public async Task Matches_rejects_a_token_that_differs_only_by_case()
        {
            var store = new SettingsAccessTokenStore(_path);
            var token = await store.GetAsync(CancellationToken.None);

            Assert.False(store.Matches(token.ToLowerInvariant(), token));
        }

        #endregion

        #region Cookie signing

        [Fact]
        public void Cookie_round_trips()
        {
            var signer = new SettingsCookieSigner("secret-token");
            var now = DateTimeOffset.UtcNow;

            Assert.True(signer.Validate(signer.Issue(now), now));
        }

        [Fact]
        public void Cookie_signed_with_another_key_is_rejected()
        {
            var issued = new SettingsCookieSigner("secret-token").Issue(DateTimeOffset.UtcNow);

            Assert.False(new SettingsCookieSigner("different-token").Validate(issued, DateTimeOffset.UtcNow));
        }

        [Fact]
        public void Tampered_payload_is_rejected()
        {
            var signer = new SettingsCookieSigner("secret-token");
            var cookie = signer.Issue(DateTimeOffset.UtcNow);
            var separator = cookie.IndexOf('.');

            var forged = cookie[..separator] + ".DEADBEEF" + cookie[(separator + 1)..];

            Assert.False(signer.Validate(forged, DateTimeOffset.UtcNow));
        }

        [Fact]
        public void Expired_cookie_is_rejected()
        {
            var signer = new SettingsCookieSigner("secret-token");
            var issued = signer.Issue(DateTimeOffset.UtcNow);

            Assert.False(signer.Validate(issued, DateTimeOffset.UtcNow.AddDays(31)));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("nodot")]
        [InlineData(".onlysig")]
        [InlineData("onlypayload.")]
        public void Malformed_cookies_are_rejected(string? cookie)
        {
            Assert.False(new SettingsCookieSigner("secret-token").Validate(cookie, DateTimeOffset.UtcNow));
        }

        #endregion

        #region Language parsing

        [Fact]
        public void SplitLanguages_trims_and_drops_blanks()
        {
            var result = SettingsPageHtml.SplitLanguages(" en , ,de ,");

            Assert.Equal(new[] { "en", "de" }, result);
        }

        [Fact]
        public void SplitLanguages_deduplicates_case_insensitively()
        {
            var result = SettingsPageHtml.SplitLanguages("en,EN,De");

            Assert.Equal(new[] { "en", "De" }, result);
        }

        [Fact]
        public void SplitLanguages_caps_the_entry_count()
        {
            var many = string.Join(",", Enumerable.Range(0, 120));

            Assert.Equal(50, SettingsPageHtml.SplitLanguages(many).Count);
        }

        [Fact]
        public void SplitLanguages_handles_empty_input()
        {
            Assert.Empty(SettingsPageHtml.SplitLanguages(string.Empty));
            Assert.Empty(SettingsPageHtml.SplitLanguages("   "));
        }

        #endregion

        #region Rendering

        [Fact]
        public void Locked_page_offers_the_token_form_and_no_settings_fields()
        {
            var html = SettingsPageHtml.Render(null, null, null, "en", Translations.For("en"));

            Assert.Contains("name=\"token\"", html);
            Assert.DoesNotContain("name=\"WatchlistPath\"", html);
        }

        [Fact]
        public void Unlocked_page_renders_current_values()
        {
            var configuration = new PluginConfiguration
            {
                WatchlistPath = "/data/watchlist",
                PreferredQuality = "sd",
                SyncIntervalHours = 12,
                PreferredAudioLanguages = new List<string> { "en", "de" },
                DownloadSubtitles = true
            };

            var html = SettingsPageHtml.Render(configuration, null, null, "en", Translations.For("en"));

            Assert.Contains("value=\"/data/watchlist\"", html);
            Assert.Contains("value=\"12\"", html);
            Assert.Contains("value=\"en, de\"", html);
            Assert.Contains("checked", html);
            Assert.DoesNotContain("name=\"token\"", html);
        }

        [Fact]
        public void Values_are_html_encoded()
        {
            var configuration = new PluginConfiguration
            {
                WatchlistPath = "\"><script>alert(1)</script>"
            };

            var html = SettingsPageHtml.Render(configuration, null, null, "en", Translations.For("en"));

            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;script&gt;", html);
        }

        [Fact]
        public void Messages_are_html_encoded()
        {
            var html = SettingsPageHtml.Render(null, null, "<img src=x onerror=alert(1)>", "en", Translations.For("en"));

            Assert.DoesNotContain("<img src=x", html);
            Assert.Contains("&lt;img", html);
        }

[Fact]
        public void Quality_is_rendered_so_saving_cannot_wipe_it()
        {
            var html = SettingsPageHtml.Render(new PluginConfiguration { PreferredQuality = "sd" }, null, null, "en", Translations.For("en"));

            Assert.Contains("name=\"PreferredQuality\"", html);
            Assert.Contains("value=\"sd\" selected", html);
        }

        [Fact]
        public void Quality_falls_back_to_hd_when_unset()
        {
            var html = SettingsPageHtml.Render(new PluginConfiguration(), null, null, "en", Translations.For("en"));

            Assert.Contains("value=\"hd\" selected", html);
        }

        [Fact]
        public void Notice_is_shown_when_saved()
        {
            var html = SettingsPageHtml.Render(new PluginConfiguration(), "Settings saved.", null, "en", Translations.For("en"));

            Assert.Contains("Settings saved.", html);
        }

        [Fact]
        public void Rendered_form_posts_without_javascript()
        {
            var html = SettingsPageHtml.Render(new PluginConfiguration(), null, null, "en", Translations.For("en"));

            Assert.Contains("method=\"post\"", html);
            Assert.DoesNotContain("<script", html);
            Assert.DoesNotContain("onsubmit", html);
            Assert.DoesNotContain("addEventListener", html);
        }

        #endregion
    }
}
