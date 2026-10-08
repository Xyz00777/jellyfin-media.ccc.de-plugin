using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Controllers;
using Jellyfin.Plugin.MediaCccDe.Services;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class UserSettingsPageTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;

            public StubHandler(HttpStatusCode status, string body)
            {
                _status = status;
                _body = body;
            }

            public HttpRequestMessage? LastRequest { get; private set; }

            public string? LastAuthorization { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                LastAuthorization = request.Headers.Authorization?.ToString();

                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body)
                });
            }
        }

        private static (JellyfinIdentityVerifier Verifier, StubHandler Handler) BuildVerifier(HttpStatusCode status, string body)
        {
            var handler = new StubHandler(status, body);
            var factory = new StubFactory(handler);
            return (new JellyfinIdentityVerifier(factory, "http://127.0.0.1:8096"), handler);
        }

        private sealed class StubFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;

            public StubFactory(HttpMessageHandler handler) => _handler = handler;

            public HttpClient CreateClient(string name) => new HttpClient(_handler, disposeHandler: false);
        }

        private const string ValidBody = "[{\"Id\":\"c07a505e-4731-415e-a5b3-f8a60a4f508a\",\"Name\":\"admin\"}]";

        #region Identity verification

        [Fact]
        public async Task Valid_key_resolves_the_user_from_the_server_response()
        {
            var (verifier, _) = BuildVerifier(HttpStatusCode.OK, ValidBody);

            var identity = await verifier.VerifyAsync("0123456789abcdef0123456789abcdef", CancellationToken.None);

            Assert.NotNull(identity);
            Assert.Equal(Guid.Parse("c07a505e-4731-415e-a5b3-f8a60a4f508a"), identity!.Id);
            Assert.Equal("admin", identity.Name);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task Rejected_by_jellyfin_means_no_identity(HttpStatusCode status)
        {
            var (verifier, _) = BuildVerifier(status, string.Empty);

            Assert.Null(await verifier.VerifyAsync("0123456789abcdef0123456789abcdef", CancellationToken.None));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-hex-key")]
        [InlineData("0123456789abcdef0123456789abcdeZ")]
        [InlineData("0123456789abcdef 0123456789abcdef")]
        public async Task Malformed_keys_never_reach_the_network(string? key)
        {
            var (verifier, handler) = BuildVerifier(HttpStatusCode.OK, ValidBody);

            Assert.Null(await verifier.VerifyAsync(key, CancellationToken.None));
            Assert.Null(handler.LastRequest);
        }

        [Fact]
        public async Task Overlong_key_never_reaches_the_network()
        {
            var (verifier, handler) = BuildVerifier(HttpStatusCode.OK, ValidBody);

            Assert.Null(await verifier.VerifyAsync(new string('a', 500), CancellationToken.None));
            Assert.Null(handler.LastRequest);
        }

        [Fact]
        public async Task Key_is_sent_as_an_authorization_header_not_in_the_query()
        {
            var (verifier, handler) = BuildVerifier(HttpStatusCode.OK, ValidBody);

            await verifier.VerifyAsync("0123456789abcdef0123456789abcdef", CancellationToken.None);

            Assert.NotNull(handler.LastAuthorization);
            Assert.Contains("Token=", handler.LastAuthorization!);
            Assert.NotNull(handler.LastRequest!.RequestUri);
            Assert.DoesNotContain("0123456789abcdef", handler.LastRequest!.RequestUri!.Query);
        }

        [Fact]
        public async Task Response_without_a_usable_id_is_rejected()
        {
            var (verifier, _) = BuildVerifier(HttpStatusCode.OK, "[{\"Name\":\"admin\"}]");

            Assert.Null(await verifier.VerifyAsync("0123456789abcdef0123456789abcdef", CancellationToken.None));
        }

        [Fact]
        public async Task Empty_guid_is_rejected()
        {
            var (verifier, _) = BuildVerifier(HttpStatusCode.OK, "[{\"Id\":\"00000000-0000-0000-0000-000000000000\",\"Name\":\"x\"}]");

            Assert.Null(await verifier.VerifyAsync("0123456789abcdef0123456789abcdef", CancellationToken.None));
        }


        [Fact]
        public async Task An_administrator_token_listing_several_users_is_refused()
        {
            var twoUsers = "[{\"Id\":\"c07a505e-4731-415e-a5b3-f8a60a4f508a\",\"Name\":\"admin\"},"
                + "{\"Id\":\"11111111-2222-3333-4444-555555555555\",\"Name\":\"someone\"}]";
            var (verifier, _) = BuildVerifier(HttpStatusCode.OK, twoUsers);

            Assert.Null(await verifier.VerifyAsync("0123456789abcdef0123456789abcdef", CancellationToken.None));
        }

        [Fact]
        public async Task An_empty_user_list_is_refused()
        {
            var (verifier, _) = BuildVerifier(HttpStatusCode.OK, "[]");

            Assert.Null(await verifier.VerifyAsync("0123456789abcdef0123456789abcdef", CancellationToken.None));
        }

        [Fact]
        public async Task A_non_array_body_is_refused()
        {
            var (verifier, _) = BuildVerifier(HttpStatusCode.OK, "{\"Id\":\"c07a505e-4731-415e-a5b3-f8a60a4f508a\"}");

            Assert.Null(await verifier.VerifyAsync("0123456789abcdef0123456789abcdef", CancellationToken.None));
        }

        #endregion

        #region User cookie binding

        [Fact]
        public void Cookie_round_trips_with_its_user()
        {
            var signer = new UserCookieSigner("token");
            var userId = Guid.Parse("c07a505e-4731-415e-a5b3-f8a60a4f508a");

            Assert.True(signer.Validate(signer.Issue(userId, DateTimeOffset.UtcNow), DateTimeOffset.UtcNow, out var resolved));
            Assert.Equal(userId, resolved);
        }

        [Fact]
        public void Cookie_cannot_be_replayed_for_a_different_user()
        {
            var signer = new UserCookieSigner("token");
            var cookie = signer.Issue(Guid.Parse("c07a505e-4731-415e-a5b3-f8a60a4f508a"), DateTimeOffset.UtcNow);
            var other = Guid.Parse("11111111-2222-3333-4444-555555555555");

            // The user id is inside the signed payload, so substituting it invalidates
            // the signature rather than granting access to the other account.
            var tampered = cookie.Replace("c07a505e4731415ea5b3f8a60a4f508a", other.ToString("N"));

            Assert.False(signer.Validate(tampered, DateTimeOffset.UtcNow, out var resolved));
            Assert.Equal(Guid.Empty, resolved);
        }

        [Fact]
        public void Cookie_signed_with_another_key_is_rejected()
        {
            var cookie = new UserCookieSigner("other-token")
                .Issue(Guid.NewGuid(), DateTimeOffset.UtcNow);

            Assert.False(new UserCookieSigner("token").Validate(cookie, DateTimeOffset.UtcNow, out _));
        }

        [Fact]
        public void Expired_cookie_is_rejected()
        {
            var signer = new UserCookieSigner("token");
            var cookie = signer.Issue(Guid.NewGuid(), DateTimeOffset.UtcNow);

            Assert.False(signer.Validate(cookie, DateTimeOffset.UtcNow.AddDays(91), out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData(".sig")]
        [InlineData("payload.")]
        public void Malformed_cookies_are_rejected(string? cookie)
        {
            Assert.False(new UserCookieSigner("token").Validate(cookie, DateTimeOffset.UtcNow, out _));
        }

        [Fact]
        public void Cookie_rejects_a_payload_without_a_user()
        {
            var signer = new UserCookieSigner("token");
            var forged = "1234567890." + Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("token"), System.Text.Encoding.UTF8.GetBytes("1234567890")));

            Assert.False(signer.Validate(forged, DateTimeOffset.UtcNow, out _));
        }

        #endregion

        #region Rendering

        [Fact]
        public void Anonymous_page_asks_for_an_api_key_and_shows_no_language_fields()
        {
            var html = UserSettingsPageHtml.Render(null, null, null, null, null, "en", Translations.For("en"));

            Assert.Contains("name=\"apikey\"", html);
            Assert.DoesNotContain("name=\"audio\"", html);
            Assert.DoesNotContain("<script", html);
        }

        [Fact]
        public void Identified_page_shows_the_name_and_current_languages()
        {
            var html = UserSettingsPageHtml.Render("admin", new[] { "en", "de" }, new[] { "de" }, null, null, "en", Translations.For("en"));

            Assert.Contains("admin", html);
            Assert.Contains("value=\"en, de\"", html);
            Assert.DoesNotContain("name=\"apikey\"", html);
            Assert.DoesNotContain("<script", html);
        }

        [Fact]
        public void Api_key_input_is_a_password_field_and_never_prefilled()
        {
            var html = UserSettingsPageHtml.Render(null, null, null, null, null, "en", Translations.For("en"));

            Assert.Contains("type=\"password\"", html);
            Assert.DoesNotContain("value=\"0123", html);
        }

        [Fact]
        public void User_name_is_html_encoded()
        {
            var html = UserSettingsPageHtml.Render("<img src=x onerror=alert(1)>", null, null, null, null, "en", Translations.For("en"));

            Assert.DoesNotContain("<img src=x", html);
            Assert.Contains("&lt;img", html);
        }

        [Fact]
        public void SplitLanguages_filters_overlong_and_duplicate_entries()
        {
            var result = UserSettingsPageHtml.SplitLanguages("en, EN, " + new string('x', 40) + ", de");

            Assert.Equal(new[] { "en", "de" }, result);
        }

        [Fact]
        public void SplitLanguages_handles_null()
        {
            Assert.Empty(UserSettingsPageHtml.SplitLanguages(null));
        }

        #endregion
    }
}
