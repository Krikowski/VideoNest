using MongoDB.Bson;
using MongoDB.Driver;
using System.Threading.Tasks;
using VideoNest.Models;

namespace VideoNest.Repositories {
    public class VideoRepository : IVideoRepository {
        private readonly IMongoCollection<VideoResult> _videos;
        private readonly IMongoCollection<VideoNest.Models.VideoCounter> _counters;

        public VideoRepository(IMongoDatabase mongoDatabase) {
            _videos = mongoDatabase.GetCollection<VideoResult>("VideoResults");
            _counters = mongoDatabase.GetCollection<VideoNest.Models.VideoCounter>("counters");
        }

        public async Task<int> GetNextIdAsync() {
            var filter = Builders<VideoNest.Models.VideoCounter>.Filter.Eq(c => c.Id, "video_counter");
            var update = Builders<VideoNest.Models.VideoCounter>.Update.Inc(c => c.Sequence, 1);
            var options = new FindOneAndUpdateOptions<VideoNest.Models.VideoCounter> {
                ReturnDocument = ReturnDocument.After,
                IsUpsert = true
            };
            var counter = await _counters.FindOneAndUpdateAsync(filter, update, options);
            return counter.Sequence;
        }

        public async Task SaveVideoAsync(VideoResult video) {
            await _videos.InsertOneAsync(video);
        }

        public async Task<VideoResult?> GetVideoByIdAsync(int id) {
            return await _videos.Find(v => v.VideoId == id).FirstOrDefaultAsync();
        }

        public async Task UpdateStatusAsync(int videoId, string status, string? errorMessage = null, int duration = 0) {
            var filter = Builders<VideoResult>.Filter.Eq(v => v.VideoId, videoId);
            var update = Builders<VideoResult>.Update
                .Set(v => v.Status, status)
                .Set(v => v.ErrorMessage, errorMessage)
                .Set(v => v.Duration, duration);
            await _videos.UpdateOneAsync(filter, update);
        }

        public async Task AddQRCodesAsync(int videoId, List<QRCodeResult> qrs) {
            var filter = Builders<VideoResult>.Filter.Eq(v => v.VideoId, videoId);
            var update = Builders<VideoResult>.Update.PushEach(v => v.QRCodes, qrs);
            await _videos.UpdateOneAsync(filter, update);
        }
    }
}