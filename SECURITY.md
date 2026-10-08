# Security policy

## Supported versions

Security fixes are provided for the current published plugin version line, `1.1.2`, which requires Jellyfin 12.2 or newer and .NET 10.

## Security details

The settings access token is a 32-byte random value generated at runtime and stored in `settings-access.txt` under the plugin configuration path. **The token is never written to the server log.** Startup logs print only the settings URL and the path of that file, because logs are routinely exported, attached to support requests, and archived. Anyone who reads the file can edit plugin settings, so treat it as a secret and protect the plugin configuration directory accordingly; on Linux and macOS the file is created readable only by the account running Jellyfin. Delete the file and restart Jellyfin to rotate the token, which invalidates every plugin session cookie.

Cookie-signing HMAC keys are derived at runtime with SHA-256 from that token. No signing secret is committed to this repository.

The settings unlock form and the per-user language, browse, and watchlist pages accept a bearer secret. These endpoints only accept it over HTTPS and refuse a cleartext request from any non-loopback client with `426 Upgrade Required`; a loopback request is exempt so a local install keeps working. All plugin session cookies are issued with `Secure`, `HttpOnly`, and `SameSite=Strict`. When Jellyfin runs behind a reverse proxy, configure the proxy's forwarded-protocol headers so the plugin sees the original scheme.

The per-user language, browse, and watchlist pages ask each user for their own Jellyfin username and password. They are verified against the server, then discarded; neither is stored or logged, and the identity is taken from Jellyfin's response rather than from anything the form claimed. An API key is not usable here: Jellyfin 12 stores keys globally with no user association, so a key cannot identify one account. The signed cookie that replaces it is bound to one Jellyfin user id, and every cookie-authenticated request re-checks that the account still exists and is enabled, so disabling or deleting an account revokes plugin access immediately.

Watchlist downloads can be queued by any authenticated user, so storage consumption is bounded: per-user and server-wide caps on unfinished downloads, a cap on a single recording's size, a cap on one user's library size, a retention limit for finished queue items, and a reserve of free space kept on the download volume. The limits live in `Services/DownloadLimits.cs` and are enforced when a download is queued and again while it transfers.

The plugin talks to `media.ccc.de` over HTTPS and does not proxy arbitrary user-supplied URLs. Outbound URL validation is implemented in `Services/RemoteUrlValidator.cs`.

## Reporting a vulnerability

Report vulnerabilities privately using GitHub's **Report a vulnerability** or Security Advisory feature on this repository. Do not report security issues in a public issue. Please avoid including personal data, and provide reproduction steps and the affected plugin version.

This plugin currently works around [Jellyfin issue 18352](https://github.com/jellyfin/jellyfin/issues/18352): Jellyfin 12.2 cannot save subtitles for remote `.strm` items. Bugs in Jellyfin itself should be reported to Jellyfin, not this repository.
