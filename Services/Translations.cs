using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    internal sealed class Translations
    {
        private static readonly IReadOnlyDictionary<string, string> English = CreateEnglish();
        private static readonly IReadOnlyDictionary<string, string> German = CreateGerman();
        private static readonly IReadOnlyCollection<string> EnglishKeySet = Array.AsReadOnly(System.Linq.Enumerable.ToArray(English.Keys));
        private static readonly IReadOnlyCollection<string> GermanKeySet = Array.AsReadOnly(System.Linq.Enumerable.ToArray(German.Keys));

        private readonly IReadOnlyDictionary<string, string> _selected;

        private Translations(string language)
        {
            Language = language;
            _selected = string.Equals(language, "de", StringComparison.Ordinal) ? German : English;
        }

        public string Language { get; }

        public static IReadOnlyList<string> SupportedLanguages => PluginLanguage.SupportedLanguages;

        public static IReadOnlyCollection<string> EnglishKeys => EnglishKeySet;

        public static IReadOnlyCollection<string> GermanKeys => GermanKeySet;

        public string this[string key] => _selected.TryGetValue(key, out var value)
            ? value
            : English.TryGetValue(key, out var fallback) ? fallback : key;

        public static Translations For(string? language) => new(PluginLanguage.Resolve(language, null));

        private static IReadOnlyDictionary<string, string> CreateEnglish() => new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["chrome.titleSuffix"] = "Media.CCC.de",
            ["chrome.language"] = "Language",
            ["chrome.backToMenu"] = "Back to menu",
            ["settings.title"] = "Media.CCC.de Settings",
            ["settings.lede"] = "Served by the plugin itself, so it works on Jellyfin 12 where the dashboard does not run plugin scripts.",
            ["settings.legend.administratorSignIn"] = "Administrator sign in",
            ["settings.hint.administratorSignIn"] = "The Jellyfin dashboard cannot authenticate plugin pages, so paste the access token printed in the server log. It is shown once per install and unlocks this form for 30 days.",
            ["settings.label.accessToken"] = "Access token",
            ["settings.button.unlock"] = "Unlock",
            ["settings.legend.storage"] = "Storage",
            ["settings.label.watchlistPath"] = "Watchlist path",
            ["settings.hint.watchlistPath"] = "Directory for downloaded watchlist videos. Leave empty to use the plugin configuration directory.",
            ["settings.legend.synchronisation"] = "Synchronisation",
            ["settings.label.syncInterval"] = "Sync interval (hours)",
            ["settings.hint.syncInterval"] = "How often to check for new content. 1-168 hours.",
            ["settings.label.preferredQuality"] = "Preferred quality",
            ["settings.option.qualityHd"] = "HD (high quality)",
            ["settings.option.qualitySd"] = "SD (smaller files)",
            ["settings.hint.preferredQuality"] = "Streaming prefers h264 mp4 for compatibility, and a file containing both of your preferred languages over either single-language file.",
            ["settings.legend.languages"] = "Languages",
            ["settings.label.preferredAudioLanguages"] = "Preferred audio languages",
            ["settings.hint.preferredAudioLanguages"] = "Comma-separated ISO 639-1 codes, in order of preference. Talks use the first available match, and a file containing both languages is preferred.",
            ["settings.label.preferredSubtitleLanguages"] = "Preferred subtitle languages",
            ["settings.hint.preferredSubtitleLanguages"] = "Comma-separated ISO 639-1 codes. Used to pick a subtitle when downloading them is enabled.",
            ["settings.legend.subtitles"] = "Subtitles",
            ["settings.label.downloadSubtitles"] = "Download subtitles",
            ["settings.hint.downloadSubtitles"] = "Off by default. Jellyfin finds subtitles only on local disk beside the media file, so each enabled talk fetches its subtitle next to the .strm. Existing libraries pick subtitles up on the next scan.",
            ["common.button.save"] = "Save",
            ["settings.error.tokenRejected"] = "That token was not accepted. Copy the whole value from the server log.",
            ["settings.error.sessionExpired"] = "Your session expired. Unlock again to save.",
            ["settings.error.pluginNotLoaded"] = "The plugin instance is not loaded yet. Try again in a moment.",
            ["settings.notice.saved"] = "Settings saved. Trigger a sync to apply them to new talks.",
            ["userSettings.title"] = "Media.CCC.de Language Preferences",
            ["userSettings.lede"] = "Your own preferences, used when a talk you watchlist is downloaded for you.",
            ["userSettings.legend.identify"] = "Identify yourself",
            ["userSettings.hint.identify"] = "Jellyfin 12 cannot sign you in from this page, so confirm your account once with your own Jellyfin API key. Create one under Dashboard > Advanced > API Keys. The key is checked against your server and then discarded; it is never stored.",
            ["userSettings.label.apiKey"] = "Your Jellyfin API key",
            ["userSettings.button.continue"] = "Continue",
            ["userSettings.signedInAs"] = "Signed in as",
            ["userSettings.legend.languages"] = "Languages",
            ["userSettings.label.audioLanguages"] = "Preferred audio languages",
            ["userSettings.hint.audioLanguages"] = "Comma-separated ISO 639-1 codes in order of preference, for example en, de. Used when a watchlist talk is downloaded for you.",
            ["userSettings.label.subtitleLanguages"] = "Preferred subtitle languages",
            ["userSettings.hint.subtitleLanguages"] = "Used to pick a subtitle track when the administrator has enabled subtitle downloads.",
            ["userSettings.button.save"] = "Save",
            ["userSettings.error.keyRejected"] = "That key was not accepted by this server. Check it under Dashboard > Advanced > API Keys and try again.",
            ["userSettings.error.sessionExpired"] = "Your session expired. Confirm your API key again.",
            ["userSettings.notice.saved"] = "Saved. Watchlist downloads now use these languages.",
            ["browse.title"] = "Browse Conferences",
            ["browse.search.placeholder"] = "Search conferences...",
            ["browse.filter.allYears"] = "All Years",
            ["browse.filter.year"] = "Year",
            ["browse.loading.conferences"] = "Loading conferences...",
            ["browse.error.conferences"] = "Failed to load conferences. Please try again later.",
            ["browse.empty.conferences"] = "No conferences found.",
            ["browse.label.conference"] = "Conference",
            ["browse.untitled.conference"] = "Untitled conference",
            ["browse.label.conferenceEvents"] = "Conference events",
            ["browse.loading.events"] = "Loading events...",
            ["browse.error.events"] = "Failed to load events.",
            ["browse.empty.events"] = "No events found.",
            ["browse.untitled.event"] = "Untitled event",
            ["browse.button.addToWatchlist"] = "Add to watchlist and download",
            ["browse.status.queued"] = "Queued",
            ["browse.button.retryDownload"] = "Retry download",
            ["watchlist.title"] = "My Watchlist",
            ["watchlist.itemsInQueue"] = "items in queue",
            ["watchlist.button.startDownload"] = "Start Download",
            ["watchlist.table.title"] = "Title",
            ["watchlist.table.conference"] = "Conference",
            ["watchlist.table.status"] = "Status",
            ["watchlist.table.actions"] = "Actions",
            ["watchlist.loading"] = "Loading watchlist...",
            ["watchlist.empty.title"] = "Your watchlist is empty",
            ["watchlist.empty.detail"] = "Browse conferences and add events to your watchlist to download them for offline viewing.",
            ["common.unknown"] = "Unknown",
            ["watchlist.downloadedPercent"] = "% downloaded",
            ["watchlist.status.pending"] = "Pending",
            ["watchlist.status.downloading"] = "Downloading",
            ["watchlist.status.ready"] = "Ready",
            ["watchlist.status.failed"] = "Failed",
            ["watchlist.button.retry"] = "Retry",
            ["watchlist.button.remove"] = "Remove",
            ["watchlist.confirm.remove"] = "Remove this item from your watchlist? The downloaded file will be deleted.",
            ["watchlist.error.load"] = "Failed to load watchlist. Please try again.",
            ["watchlist.error.remove"] = "Failed to remove item. Please try again.",
            ["watchlist.syncing"] = "Syncing...",
            ["syncLog.title"] = "Sync History",
            ["syncLog.subtitle"] = "Conference archive synchronization log",
            ["syncLog.button.trigger"] = "Trigger Manual Sync",
            ["syncLog.button.refresh"] = "Refresh",
            ["syncLog.button.clear"] = "Clear History",
            ["syncLog.confirm.clear"] = "Clear all synchronization history? This cannot be undone.",
            ["syncLog.empty"] = "No synchronization history yet.",
            ["syncLog.loading"] = "Loading synchronization history..."
        });

        private static IReadOnlyDictionary<string, string> CreateGerman() => new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["chrome.titleSuffix"] = "Media.CCC.de",
            ["chrome.language"] = "Sprache",
            ["chrome.backToMenu"] = "Zurück zum Menü",
            ["settings.title"] = "Media.CCC.de-Einstellungen",
            ["settings.lede"] = "Diese Seite wird vom Plugin selbst bereitgestellt und funktioniert deshalb auch mit Jellyfin 12, dessen Dashboard keine Plugin-Skripte ausführt.",
            ["settings.legend.administratorSignIn"] = "Anmeldung für Administratoren",
            ["settings.hint.administratorSignIn"] = "Das Jellyfin-Dashboard kann Plugin-Seiten nicht authentifizieren. Füge hier den Zugriffsschlüssel aus dem Serverprotokoll ein. Er wird einmal pro Installation angezeigt und schaltet dieses Formular für 30 Tage frei.",
            ["settings.label.accessToken"] = "Zugriffsschlüssel",
            ["settings.button.unlock"] = "Freischalten",
            ["settings.legend.storage"] = "Speicher",
            ["settings.label.watchlistPath"] = "Pfad zur Merkliste",
            ["settings.hint.watchlistPath"] = "Verzeichnis für heruntergeladene Videos aus der Merkliste. Leer lassen, um das Plugin-Konfigurationsverzeichnis zu verwenden.",
            ["settings.legend.synchronisation"] = "Synchronisation",
            ["settings.label.syncInterval"] = "Synchronisationsintervall (Stunden)",
            ["settings.hint.syncInterval"] = "Wie oft nach neuen Inhalten gesucht wird. 1 bis 168 Stunden.",
            ["settings.label.preferredQuality"] = "Bevorzugte Qualität",
            ["settings.option.qualityHd"] = "HD (hohe Qualität)",
            ["settings.option.qualitySd"] = "SD (kleinere Dateien)",
            ["settings.hint.preferredQuality"] = "Beim Streaming wird aus Kompatibilitätsgründen h264 mp4 bevorzugt. Eine Datei mit beiden bevorzugten Sprachen hat Vorrang vor Dateien mit nur einer Sprache.",
            ["settings.legend.languages"] = "Sprachen",
            ["settings.label.preferredAudioLanguages"] = "Bevorzugte Audiosprachen",
            ["settings.hint.preferredAudioLanguages"] = "ISO-639-1-Sprachcodes, durch Kommas getrennt und nach Priorität sortiert. Bei Vorträgen wird der erste verfügbare Treffer verwendet; Dateien mit beiden Sprachen werden bevorzugt.",
            ["settings.label.preferredSubtitleLanguages"] = "Bevorzugte Untertitelsprachen",
            ["settings.hint.preferredSubtitleLanguages"] = "ISO-639-1-Sprachcodes, durch Kommas getrennt. Damit wird bei aktivierten Untertitel-Downloads der passende Untertitel ausgewählt.",
            ["settings.legend.subtitles"] = "Untertitel",
            ["settings.label.downloadSubtitles"] = "Untertitel herunterladen",
            ["settings.hint.downloadSubtitles"] = "Standardmäßig deaktiviert. Jellyfin erkennt Untertitel nur lokal neben der Mediendatei. Für jeden aktivierten Vortrag wird der Untertitel neben der .strm-Datei abgelegt. Beim nächsten Bibliotheksscan werden vorhandene Einträge aktualisiert.",
            ["common.button.save"] = "Speichern",
            ["settings.error.tokenRejected"] = "Der Zugriffsschlüssel wurde nicht akzeptiert. Kopiere den vollständigen Wert aus dem Serverprotokoll.",
            ["settings.error.sessionExpired"] = "Deine Sitzung ist abgelaufen. Schalte die Einstellungen erneut frei, um zu speichern.",
            ["settings.error.pluginNotLoaded"] = "Das Plugin ist noch nicht geladen. Bitte versuche es gleich noch einmal.",
            ["settings.notice.saved"] = "Einstellungen gespeichert. Starte eine Synchronisation, damit sie für neue Vorträge gelten.",
            ["userSettings.title"] = "Spracheinstellungen für Media.CCC.de",
            ["userSettings.lede"] = "Deine persönlichen Einstellungen. Sie gelten, wenn ein Vortrag aus deiner Merkliste für dich heruntergeladen wird.",
            ["userSettings.legend.identify"] = "Identität bestätigen",
            ["userSettings.hint.identify"] = "Jellyfin 12 kann dich auf dieser Seite nicht anmelden. Bestätige dein Konto deshalb einmalig mit deinem persönlichen Jellyfin-API-Schlüssel. Erstelle ihn unter Dashboard > Erweitert > API-Schlüssel. Der Schlüssel wird auf deinem Server geprüft und danach verworfen; er wird nicht gespeichert.",
            ["userSettings.label.apiKey"] = "Dein Jellyfin-API-Schlüssel",
            ["userSettings.button.continue"] = "Weiter",
            ["userSettings.signedInAs"] = "Angemeldet als",
            ["userSettings.legend.languages"] = "Sprachen",
            ["userSettings.label.audioLanguages"] = "Bevorzugte Audiosprachen",
            ["userSettings.hint.audioLanguages"] = "ISO-639-1-Sprachcodes nach Priorität, durch Kommas getrennt, zum Beispiel en, de. Diese Auswahl gilt, wenn ein Vortrag aus deiner Merkliste für dich heruntergeladen wird.",
            ["userSettings.label.subtitleLanguages"] = "Bevorzugte Untertitelsprachen",
            ["userSettings.hint.subtitleLanguages"] = "Damit wird der passende Untertitel ausgewählt, wenn die Administration Untertitel-Downloads aktiviert hat.",
            ["userSettings.button.save"] = "Speichern",
            ["userSettings.error.keyRejected"] = "Dieser Server hat den Schlüssel abgelehnt. Prüfe ihn unter Dashboard > Erweitert > API-Schlüssel und versuche es erneut.",
            ["userSettings.error.sessionExpired"] = "Deine Sitzung ist abgelaufen. Bestätige deinen API-Schlüssel erneut.",
            ["userSettings.notice.saved"] = "Gespeichert. Downloads aus deiner Merkliste verwenden jetzt diese Sprachen.",
            ["browse.title"] = "Konferenzen durchsuchen",
            ["browse.search.placeholder"] = "Konferenzen durchsuchen...",
            ["browse.filter.allYears"] = "Alle Jahre",
            ["browse.filter.year"] = "Jahr",
            ["browse.loading.conferences"] = "Konferenzen werden geladen...",
            ["browse.error.conferences"] = "Die Konferenzen konnten nicht geladen werden. Bitte versuche es später erneut.",
            ["browse.empty.conferences"] = "Keine Konferenzen gefunden.",
            ["browse.label.conference"] = "Konferenz",
            ["browse.untitled.conference"] = "Konferenz ohne Titel",
            ["browse.label.conferenceEvents"] = "Beiträge der Konferenz",
            ["browse.loading.events"] = "Beiträge werden geladen...",
            ["browse.error.events"] = "Die Beiträge konnten nicht geladen werden.",
            ["browse.empty.events"] = "Keine Beiträge gefunden.",
            ["browse.untitled.event"] = "Beitrag ohne Titel",
            ["browse.button.addToWatchlist"] = "Zur Merkliste hinzufügen und herunterladen",
            ["browse.status.queued"] = "In Warteschlange",
            ["browse.button.retryDownload"] = "Download erneut versuchen",
            ["watchlist.title"] = "Meine Merkliste",
            ["watchlist.itemsInQueue"] = "Einträge in der Warteschlange",
            ["watchlist.button.startDownload"] = "Download starten",
            ["watchlist.table.title"] = "Titel",
            ["watchlist.table.conference"] = "Konferenz",
            ["watchlist.table.status"] = "Status",
            ["watchlist.table.actions"] = "Aktionen",
            ["watchlist.loading"] = "Merkliste wird geladen...",
            ["watchlist.empty.title"] = "Deine Merkliste ist leer",
            ["watchlist.empty.detail"] = "Durchsuche die Konferenzen und füge Beiträge zu deiner Merkliste hinzu, um sie offline anzusehen.",
            ["common.unknown"] = "Unbekannt",
            ["watchlist.downloadedPercent"] = "% heruntergeladen",
            ["watchlist.status.pending"] = "Ausstehend",
            ["watchlist.status.downloading"] = "Wird heruntergeladen",
            ["watchlist.status.ready"] = "Bereit",
            ["watchlist.status.failed"] = "Fehlgeschlagen",
            ["watchlist.button.retry"] = "Erneut versuchen",
            ["watchlist.button.remove"] = "Entfernen",
            ["watchlist.confirm.remove"] = "Diesen Eintrag aus deiner Merkliste entfernen? Die heruntergeladene Datei wird gelöscht.",
            ["watchlist.error.load"] = "Die Merkliste konnte nicht geladen werden. Bitte versuche es erneut.",
            ["watchlist.error.remove"] = "Der Eintrag konnte nicht entfernt werden. Bitte versuche es erneut.",
            ["watchlist.syncing"] = "Synchronisierung läuft...",
            ["syncLog.title"] = "Synchronisationsverlauf",
            ["syncLog.subtitle"] = "Protokoll der Konferenzarchiv-Synchronisation",
            ["syncLog.button.trigger"] = "Synchronisation manuell starten",
            ["syncLog.button.refresh"] = "Aktualisieren",
            ["syncLog.button.clear"] = "Verlauf löschen",
            ["syncLog.confirm.clear"] = "Den gesamten Synchronisationsverlauf löschen? Dieser Vorgang kann nicht rückgängig gemacht werden.",
            ["syncLog.empty"] = "Noch kein Synchronisationsverlauf vorhanden.",
            ["syncLog.loading"] = "Synchronisationsverlauf wird geladen..."
        });
    }
}
