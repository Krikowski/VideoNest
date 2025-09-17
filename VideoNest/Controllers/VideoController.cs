// C:/Estudos/Hackaton_FIAP/VideoNest/VideoNest/VideosController.cs

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;
using VideoNest.Models;
using VideoNest.Repositories;
using VideoNest.Service;
using MongoDB.Driver;
using MongoDB.Bson;
using VideoNest.Models;

namespace VideoNest.Controllers {
    [ApiController]
    [Route("[controller]")]
    public class VideosController : ControllerBase {
        private readonly IVideoRepository _videoRepository;
        private readonly ILogger<VideosController> _logger;
        private readonly RabbitMQPublisher _rabbitMQPublisher;
        private readonly IMongoDatabase _mongoDatabase;

        public VideosController(IVideoRepository videoRepository, ILogger<VideosController> logger,
            RabbitMQPublisher rabbitMQPublisher, IMongoDatabase mongoDatabase) {
            _videoRepository = videoRepository;
            _logger = logger;
            _rabbitMQPublisher = rabbitMQPublisher;
            _mongoDatabase = mongoDatabase;
        }

        [HttpPost]
        public async Task<IActionResult> UploadVideo(IFormFile file) {
            if (file == null || file.Length == 0) {
                _logger.LogWarning("Nenhum arquivo enviado.");
                return BadRequest("Nenhum arquivo enviado.");
            }

            var extension = Path.GetExtension(file.FileName).ToLower();
            if (extension != ".mp4" && extension != ".avi") {
                _logger.LogWarning("Formato inválido: {Extension}", extension);
                return BadRequest("Apenas .mp4 ou .avi são permitidos.");
            }

            try {
                var fileName = Path.GetFileName(file.FileName);
                string savePath;
                string hostPath;

                if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
                    hostPath = Path.Combine("C:\\Estudos\\Hackaton_FIAP\\uploads", fileName);
                    savePath = hostPath;
                } else {
                    hostPath = savePath = Path.Combine("/uploads", fileName).Replace("\\", "/");
                }

                var directory = Path.GetDirectoryName(hostPath);
                if (!string.IsNullOrEmpty(directory)) {
                    Directory.CreateDirectory(directory);
                }

                using (var stream = new FileStream(hostPath, FileMode.Create)) {
                    await file.CopyToAsync(stream);
                }
                _logger.LogInformation("Vídeo salvo em {HostPath}", hostPath);

                var video = new VideoDB {
                    Title = fileName,
                    FilePath = savePath,
                    Status = "Na Fila",
                    Description = "Vídeo enviado",
                    Duration = 0,
                    CreatedAt = DateTime.UtcNow
                };
                await _videoRepository.SaveVideoAsync(video);
                _logger.LogInformation("Vídeo ID {VideoId} registrado com FilePath {FilePath}", video.Id, video.FilePath);

                var collection = _mongoDatabase.GetCollection<VideoResult>("VideoResults");
                var videoResult = new VideoResult { VideoId = video.Id, Status = "Na Fila" };
                await collection.InsertOneAsync(videoResult);
                _logger.LogInformation("Documento inicial criado em Mongo para vídeo ID {VideoId}", video.Id);

                var videoMessage = new {
                    VideoId = video.Id,
                    FilePath = savePath
                };
                var message = JsonConvert.SerializeObject(videoMessage);
                _rabbitMQPublisher.PublishMessage(message);
                _logger.LogInformation("Mensagem enviada para a fila video_queue: {Message}", message);

                return Ok(new { VideoId = video.Id, Message = "Vídeo enviado para processamento." });
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao processar upload do vídeo.");
                return StatusCode(500, "Erro ao processar o vídeo.");
            }
        }

        [HttpGet("{id}/status")]
        public async Task<IActionResult> GetVideoStatus(int id) {
            var collection = _mongoDatabase.GetCollection<VideoResult>("VideoResults");
            var videoResult = await collection.Find(v => v.VideoId == id).FirstOrDefaultAsync();
            if (videoResult == null) {
                _logger.LogWarning("Vídeo ID {VideoId} não encontrado em Mongo.", id);
                return NotFound("Vídeo não encontrado.");
            }
            return Ok(new { Status = videoResult.Status });
        }

        [HttpGet("{id}/results")]
        public async Task<IActionResult> GetVideoResults(int id) {
            var collection = _mongoDatabase.GetCollection<VideoResult>("VideoResults");
            var videoResult = await collection.Find(v => v.VideoId == id).FirstOrDefaultAsync();
            if (videoResult == null) {
                _logger.LogWarning("Vídeo ID {VideoId} não encontrado em Mongo.", id);
                return NotFound("Vídeo não encontrado.");
            }
            if (videoResult.Status != "Concluído") {
                return BadRequest("Processamento não concluído.");
            }
            return Ok(videoResult.QRCodes.Select(q => new { Content = q.Content, Timestamp = q.Timestamp }));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetVideoDetails(int id) {
            var video = await _videoRepository.GetVideoByIdAsync(id);
            if (video == null) {
                _logger.LogWarning("Vídeo ID {VideoId} não encontrado.", id);
                return NotFound("Vídeo não encontrado.");
            }

            return Ok(new {
                video.Id,
                video.Title,
                video.Description,
                video.Duration,
                video.FilePath,
                video.Status,
                video.CreatedAt,
                QRCodes = video.QRCodes.Select(q => new { q.Content, q.Timestamp })
            });
        }
    }
}