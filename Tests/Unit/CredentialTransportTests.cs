using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.MediaCccDe.Api;
using Jellyfin.Plugin.MediaCccDe.Controllers;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using Moq.Protected;
using Xunit;
using PluginUserData = Jellyfin.Plugin.MediaCccDe.Models.UserData;
using PluginUserDataManager = Jellyfin.Plugin.MediaCccDe.Services.IUserDataManager;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public sealed class CredentialTransportTests : SecurityTestBase
    {
        #region A credential may only be submitted over TLS

        [Fact]
        public void Credential_submission_is_allowed_over_https()
        {
            Assert.True(CredentialTransportGuard.AllowsCredentialSubmission(
                CreateRequest(isHttps: true, remoteAddress: "203.0.113.10")));
        }

        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("::1")]
        public void Credential_submission_is_allowed_over_loopback_http_for_local_installs(string address)
        {
            Assert.True(CredentialTransportGuard.AllowsCredentialSubmission(
                CreateRequest(isHttps: false, remoteAddress: address)));
        }

        [Theory]
        [InlineData("203.0.113.10")]
        [InlineData("10.0.0.5")]
        [InlineData("192.168.1.7")]
        public void Credential_submission_is_refused_for_cleartext_http_from_a_remote_client(string address)
        {
            Assert.False(CredentialTransportGuard.AllowsCredentialSubmission(
                CreateRequest(isHttps: false, remoteAddress: address)));
        }

        [Fact]
        public void Credential_submission_is_refused_when_the_peer_address_is_unknown()
        {
            Assert.False(CredentialTransportGuard.AllowsCredentialSubmission(
                CreateRequest(isHttps: false, remoteAddress: null)));
        }

        [Fact]
        public void An_ipv4_mapped_loopback_peer_counts_as_loopback()
        {
            // A local browser often arrives as ::ffff:127.0.0.1, which a naive comparison
            // against the IPv4 loopback range would wrongly treat as remote.
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:127.0.0.1");
            context.Request.Scheme = Uri.UriSchemeHttp;

            Assert.True(CredentialTransportGuard.AllowsCredentialSubmission(context.Request));
        }

        [Theory]
        [InlineData("localhost")]
        [InlineData("localhost:8096")]
        [InlineData("127.0.0.1:8096")]
        [InlineData("[::1]:8096")]
        public void A_client_that_dialled_a_loopback_name_may_submit_over_http(string host)
        {
            // Container and VM deployments report a bridge address as the peer even for a
            // browser on the host, so the name the client dialled has to count too. Without
            // this the documented local install is refused in every Docker install.
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.88.0.1");
            context.Request.Scheme = Uri.UriSchemeHttp;
            context.Request.Host = new Microsoft.AspNetCore.Http.HostString(host);

            Assert.True(CredentialTransportGuard.AllowsCredentialSubmission(context.Request));
        }

        [Theory]
        [InlineData("jellyfin.lan:8096")]
        [InlineData("media.example.com")]
        public void A_client_on_the_network_dialling_a_network_name_over_http_is_refused(string host)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");
            context.Request.Scheme = Uri.UriSchemeHttp;
            context.Request.Host = new Microsoft.AspNetCore.Http.HostString(host);

            Assert.False(CredentialTransportGuard.AllowsCredentialSubmission(context.Request));
        }

        [Fact]
        public void A_loopback_peer_may_submit_over_http_whatever_name_it_dialled()
        {
            // Reaching the server over loopback means the bytes never left the host, so a
            // loopback peer is trusted even when it used a resolvable LAN name.
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Scheme = Uri.UriSchemeHttp;
            context.Request.Host = new Microsoft.AspNetCore.Http.HostString("jellyfin.lan:8096");

            Assert.True(CredentialTransportGuard.AllowsCredentialSubmission(context.Request));
        }

        [Fact]
        public void A_proxied_cleartext_request_is_refused_even_from_a_loopback_peer()
        {
            // A proxy on this host connects from loopback, so the peer address cannot be
            // the whole story: a plain-HTTP proxy would otherwise pass while the hop to the
            // client stayed cleartext.
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Scheme = Uri.UriSchemeHttp;
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
            context.Request.Headers["X-Forwarded-Proto"] = "http";

            Assert.False(CredentialTransportGuard.AllowsCredentialSubmission(context.Request));
        }

        [Fact]
        public void A_proxied_https_request_is_still_allowed()
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Scheme = Uri.UriSchemeHttps;
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
            context.Request.Headers["X-Forwarded-Proto"] = "https";

            Assert.True(CredentialTransportGuard.AllowsCredentialSubmission(context.Request));
        }

        [Fact]
        public async Task Settings_unlock_refuses_the_access_token_over_cleartext_http()
        {
            var store = new SettingsAccessTokenStore(_root);
            var token = await store.GetAsync(CancellationToken.None);

            var request = CreateRequest(isHttps: false, remoteAddress: "203.0.113.10");
            request.Form = new FormCollection(new Dictionary<string, StringValues>
            {
                ["token"] = token
            });

            var result = await CreateSettingsController(request, store).Unlock(CancellationToken.None);

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status426UpgradeRequired, objectResult.StatusCode);
            Assert.Equal(0, request.HttpContext.Response.Headers["Set-Cookie"].Count);
        }

        [Fact]
        public async Task Settings_unlock_issues_a_secure_cookie_over_https()
        {
            var store = new SettingsAccessTokenStore(_root);
            var token = await store.GetAsync(CancellationToken.None);

            var request = CreateRequest(isHttps: true, remoteAddress: "203.0.113.10");
            request.Form = new FormCollection(new Dictionary<string, StringValues>
            {
                ["token"] = token
            });

            await CreateSettingsController(request, store).Unlock(CancellationToken.None);

            var cookie = request.HttpContext.Response.Headers["Set-Cookie"].ToString();
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Settings_unlock_over_loopback_http_still_issues_a_secure_cookie()
        {
            var store = new SettingsAccessTokenStore(_root);
            var token = await store.GetAsync(CancellationToken.None);

            var request = CreateRequest(isHttps: false, remoteAddress: "127.0.0.1");
            request.Form = new FormCollection(new Dictionary<string, StringValues>
            {
                ["token"] = token
            });

            await CreateSettingsController(request, store).Unlock(CancellationToken.None);

            Assert.Contains(
                "secure",
                request.HttpContext.Response.Headers["Set-Cookie"].ToString(),
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task User_settings_identify_refuses_the_api_key_over_cleartext_http()
        {
            var request = CreateRequest(isHttps: false, remoteAddress: "203.0.113.10");
            request.Form = new FormCollection(new Dictionary<string, StringValues>
            {
                ["apikey"] = "0123456789abcdef0123456789abcdef"
            });

            var controller = new UserSettingsController(
                CreateIdentityVerifier(HttpStatusCode.OK),
                new SettingsAccessTokenStore(_root),
                new Mock<PluginUserDataManager>(MockBehavior.Loose).Object,
                CreateUserManager(CreateUser(disabled: false)).Object)
            {
                ControllerContext = new ControllerContext { HttpContext = request.HttpContext }
            };

            var result = await controller.Identify(CancellationToken.None);

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status426UpgradeRequired, objectResult.StatusCode);
            Assert.Equal(0, request.HttpContext.Response.Headers["Set-Cookie"].Count);
        }

        [Fact]
        public async Task A_newly_issued_session_cookie_is_marked_secure()
        {
            var store = new SettingsAccessTokenStore(_root);
            var user = CreateUser(disabled: false);

            var request = CreateRequest(isHttps: false, remoteAddress: "127.0.0.1");
            var session = new UserPageSession(
                store,
                new Mock<PluginUserDataManager>(MockBehavior.Loose).Object,
                CreateUserManager(user).Object);

            await session.IssueCookieAsync(
                request.HttpContext.Response,
                request,
                user.Id,
                CancellationToken.None);

            var cookie = request.HttpContext.Response.Headers["Set-Cookie"].ToString();
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
