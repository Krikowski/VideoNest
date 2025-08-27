using VideoNest.Models;

namespace VideoNest.Repositories {
    public interface IVideoRepository {
        Task SaveVideoAsync(VideoDB video);
        Task<VideoDB> GetVideoByIdAsync(int id);
    }
}