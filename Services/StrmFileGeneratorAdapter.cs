using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Models;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Adapter that implements <see cref="IStrmFileGenerator"/> by delegating
    /// recording selection to <see cref="IRecordingSelector"/> and writing
    /// .strm files to the caller-specified directory.
    /// </summary>
    public class StrmFileGeneratorAdapter : IStrmFileGenerator
    {
        private readonly IRecordingSelector _recordingSelector;

        public StrmFileGeneratorAdapter(IRecordingSelector recordingSelector)
        {
            _recordingSelector = recordingSelector ?? throw new ArgumentNullException(nameof(recordingSelector));
        }

        public async Task GenerateStrmAsync(string directory, EventDto @event, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(directory))
            {
                throw new ArgumentException("Directory cannot be null or empty", nameof(directory));
            }

            if (@event == null)
            {
                throw new ArgumentNullException(nameof(@event));
            }

            if (@event.Recordings == null || !@event.Recordings.Any())
            {
                return;
            }

            var recordings = @event.Recordings.Select(MapToRecording).ToList();
            var recording = _recordingSelector.SelectBestRecording(recordings, null);
            if (recording == null)
            {
                return;
            }

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var sanitizedSlug = StrmHelper.SanitizeFileName(@event.Slug ?? @event.Guid ?? "unknown");
            var filePath = Path.Combine(directory, $"{sanitizedSlug}.strm");

            if (File.Exists(filePath) && string.Equals(
                    await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false),
                    recording.Url,
                    StringComparison.Ordinal))
            {
                return;
            }

            await File.WriteAllTextAsync(filePath, recording.Url, cancellationToken).ConfigureAwait(false);
        }

        private static Recording MapToRecording(RecordingDto dto)
        {
            return new Recording
            {
                Id = dto.Id,
                Size = dto.Size ?? 0,
                Length = dto.Length ?? 0,
                MimeType = dto.EffectiveMimeType ?? string.Empty,
                Language = dto.Language ?? string.Empty,
                Url = dto.EffectiveUrl,
                Format = dto.EffectiveFormat,
                HighQuality = dto.HighQuality,
                Width = dto.Width,
                Height = dto.Height,
                FileSize = dto.FileSize,
                Bitrate = dto.Bitrate
            };
        }
    }
}
