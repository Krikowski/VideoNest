using Microsoft.AspNetCore.SignalR;
using VideoNest.Models;

namespace VideoNest.Hubs {
    public class VideoHub : Hub {
        public async Task VideoProcessed(int videoId, string status, List<QRCodeResult>? qrs = null) {
            await Clients.All.SendAsync("VideoProcessed", new { VideoId = videoId, Status = status, QRCodes = qrs });
        }
    }
}