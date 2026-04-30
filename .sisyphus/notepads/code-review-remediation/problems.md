# Unresolved Problems — Code Review Remediation

## Open questions:
- Should `.sisyphus/` be gitignored? Currently tracked. Decision needed in P7-3.
- What should `DeriveParentIndexNumber` do for non-December conferences? The review found a bug, but the correct fix depends on how media.ccc.de structures multi-day events outside CCC congresses. May need API inspection.
- What should episode numbering use instead of GetHashCode? Options: sequential within conference day, position in API response, stable hash (SHA256 of slug). Needs product decision.
- For P6-4 (integration tests), what's preferred: WireMock recording, live test endpoint, or honest rename?
