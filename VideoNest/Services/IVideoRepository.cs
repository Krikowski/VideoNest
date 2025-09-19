using VideoNest.Models;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace VideoNest.Repositories {
    public interface IVideoRepository {
        Task<int> GetNextIdAsync();
        Task SaveVideoAsync(VideoResult video);
        Task<VideoResult?> GetVideoByIdAsync(int id);
        Task UpdateStatusAsync(int videoId, string status, string? errorMessage = null, int duration = 0);
        Task AddQRCodesAsync(int videoId, List<QRCodeResult> qrs);
    }
}