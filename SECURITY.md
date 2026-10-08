# Security policy

## Supported versions

Security fixes are provided for the current published plugin version line, `1.1.0`, which requires Jellyfin 12.2 or newer and .NET 10.

## Security details

On startup, the plugin prints a settings access token to the Jellyfin server log and stores it in `settings-access.txt` under the plugin configuration path. The token is a 32-byte random value generated at runtime. Anyone who has it can edit plugin settings, so treat it as a secret and protect server logs and plugin configuration data accordingly.

Cookie-signing HMAC keys are derived at runtime with SHA-256 from that token. No signing secret is committed to this repository.

The per-user language page asks each user for their own Jellyfin API key. The key is verified against the server, then discarded. It is never stored or logged.

The plugin talks to `media.ccc.de` over HTTPS and does not proxy arbitrary user-supplied URLs. Outbound URL validation is implemented in `Services/RemoteUrlValidator.cs`.

## Reporting a vulnerability

Report vulnerabilities privately using GitHub's **Report a vulnerability** or Security Advisory feature on this repository. Do not report security issues in a public issue. Please avoid including personal data, and provide reproduction steps and the affected plugin version.

This plugin currently works around [Jellyfin issue 18352](https://github.com/jellyfin/jellyfin/issues/18352): Jellyfin 12.2 cannot save subtitles for remote `.strm` items. Bugs in Jellyfin itself should be reported to Jellyfin, not this repository.
