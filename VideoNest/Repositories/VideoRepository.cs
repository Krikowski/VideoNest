using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using VideoNest.Data;
using VideoNest.Models;

namespace VideoNest.Repositories {
    public class VideoRepository : IVideoRepository {
        private readonly VideoDbContext _dbContext;

        public VideoRepository(VideoDbContext dbContext) {
            _dbContext = dbContext;
        }

        public async Task SaveVideoAsync(VideoDB video) {
            _dbContext.Videos.Add(video);
            await _dbContext.SaveChangesAsync();
        }

        public async Task<VideoDB?> GetVideoByIdAsync(int id) {
            return await _dbContext.Videos
                .Include(v => v.QRCodes)
                .FirstOrDefaultAsync(v => v.Id == id);
        }
    }
}