namespace Jellyfin.Plugin.MediaCccDe.Services
{
    public interface ISyncTrigger
    {
        Task TriggerSyncAsync(CancellationToken cancellationToken = default);
    }
}
