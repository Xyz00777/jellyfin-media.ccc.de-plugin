using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Models;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public interface IStrmGenerator
    {
        Task<StrmResult?> GenerateStrmAsync(Conference conference, Event evt, CancellationToken cancellationToken);
        Task<List<StrmResult>> GenerateSeriesStrmTreeAsync(CancellationToken cancellationToken);
        Task CreateStrmFilesForConference(ConferenceDto conference, CancellationToken cancellationToken);
        bool StrmFilesExistForConference(ConferenceDto conference);
    }
}