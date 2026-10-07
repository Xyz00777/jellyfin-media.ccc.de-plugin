using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Models;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public interface IWatchlistDownloadService
    {
        Task<DownloadQueueItem?> EnqueueAsync(Guid userId, string eventGuid, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<DownloadQueueItem>> GetUserQueueAsync(Guid userId);
    }
}
