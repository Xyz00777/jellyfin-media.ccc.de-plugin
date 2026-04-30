namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Result of tree generation operation.
    /// </summary>
    public class TreeGenerationResult
    {
        /// <summary>
        /// Number of .strm files created.
        /// </summary>
        public int FilesCreated { get; set; }

        /// <summary>
        /// Number of conferences processed.
        /// </summary>
        public int ConferencesProcessed { get; set; }

        /// <summary>
        /// Number of conferences that failed.
        /// </summary>
        public int FailedConferences { get; set; }

        /// <summary>
        /// Number of season folders created.
        /// </summary>
        public int SeasonsCreated { get; set; }

        /// <summary>
        /// Number of series folders created.
        /// </summary>
        public int SeriesFoldersCreated { get; set; }
    }
}