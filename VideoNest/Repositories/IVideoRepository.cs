using VideoNest.Models;
using System.Threading.Tasks;

namespace VideoNest.Repositories {
    public interface IVideoRepository {
        Task SaveVideoAsync(VideoDB video);
        Task<VideoDB?> GetVideoByIdAsync(int id); 
    }
}