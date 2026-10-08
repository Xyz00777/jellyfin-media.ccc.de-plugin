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
    public sealed class PluginSessionRevocationTests : SecurityTestBase
    {
        #region Disabling or deleting an account revokes the plugin session

        [Fact]
        public void An_enabled_account_is_active()
        {
            var user = CreateUser(disabled: false);

            Assert.True(PluginUserAccess.IsActive(CreateUserManager(user).Object, user.Id));
        }

        [Fact]
        public void A_disabled_account_is_not_active()
        {
            var user = CreateUser(disabled: true);

            Assert.False(PluginUserAccess.IsActive(CreateUserManager(user).Object, user.Id));
        }

        [Fact]
        public void A_deleted_account_is_not_active()
        {
            Assert.False(PluginUserAccess.IsActive(CreateUserManager(null).Object, Guid.NewGuid()));
        }

        [Fact]
        public void An_empty_user_id_is_never_active()
        {
            Assert.False(PluginUserAccess.IsActive(CreateUserManager(CreateUser(disabled: false)).Object, Guid.Empty));
        }

        [Fact]
        public async Task A_cookie_still_resolves_while_the_account_is_enabled()
        {
            var user = CreateUser(disabled: false);
            var store = new SettingsAccessTokenStore(_root);
            var token = await store.GetAsync(CancellationToken.None);

            var request = CreateRequest(isHttps: true, remoteAddress: "203.0.113.10");
            SetCookie(request, UserCookieSigner.CookieName, new UserCookieSigner(token).Issue(user.Id, DateTimeOffset.UtcNow));

            var session = new UserPageSession(
                store,
                new Mock<PluginUserDataManager>(MockBehavior.Loose).Object,
                CreateUserManager(user).Object);

            var identity = await session.ResolveAsync(request, CancellationToken.None);

            Assert.NotNull(identity);
            Assert.Equal(user.Id, identity!.Id);
        }

        [Fact]
        public async Task A_cookie_stops_resolving_once_the_account_is_disabled()
        {
            var user = CreateUser(disabled: true);
            var store = new SettingsAccessTokenStore(_root);
            var token = await store.GetAsync(CancellationToken.None);

            var request = CreateRequest(isHttps: true, remoteAddress: "203.0.113.10");
            SetCookie(request, UserCookieSigner.CookieName, new UserCookieSigner(token).Issue(user.Id, DateTimeOffset.UtcNow));

            var session = new UserPageSession(
                store,
                new Mock<PluginUserDataManager>(MockBehavior.Loose).Object,
                CreateUserManager(user).Object);

            Assert.Null(await session.ResolveAsync(request, CancellationToken.None));
        }

        [Fact]
        public async Task A_cookie_stops_resolving_once_the_account_is_deleted()
        {
            var store = new SettingsAccessTokenStore(_root);
            var token = await store.GetAsync(CancellationToken.None);
            var orphan = Guid.NewGuid();

            var request = CreateRequest(isHttps: true, remoteAddress: "203.0.113.10");
            SetCookie(request, UserCookieSigner.CookieName, new UserCookieSigner(token).Issue(orphan, DateTimeOffset.UtcNow));

            var session = new UserPageSession(
                store,
                new Mock<PluginUserDataManager>(MockBehavior.Loose).Object,
                CreateUserManager(null).Object);

            Assert.Null(await session.ResolveAsync(request, CancellationToken.None));
        }

        [Fact]
        public async Task User_settings_save_is_refused_once_the_account_is_disabled()
        {
            var user = CreateUser(disabled: true);
            var store = new SettingsAccessTokenStore(_root);
            var token = await store.GetAsync(CancellationToken.None);

            var request = CreateRequest(isHttps: true, remoteAddress: "203.0.113.10");
            SetCookie(request, UserCookieSigner.CookieName, new UserCookieSigner(token).Issue(user.Id, DateTimeOffset.UtcNow));

            var controller = new UserSettingsController(
                CreateIdentityVerifier(HttpStatusCode.OK),
                store,
                new Mock<PluginUserDataManager>(MockBehavior.Loose).Object,
                CreateUserManager(user).Object)
            {
                ControllerContext = new ControllerContext { HttpContext = request.HttpContext }
            };

            var result = await controller.Save(CancellationToken.None);

            var content = Assert.IsType<ContentResult>(result);
            Assert.Contains(
                Translations.For("en")["userSettings.error.sessionExpired"],
                content.Content!,
                StringComparison.Ordinal);
        }

        #endregion
    }
}
