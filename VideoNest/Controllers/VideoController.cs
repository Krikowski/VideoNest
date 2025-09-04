using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using VideoNest.Models;
using VideoNest.Repositories;
using VideoNest.Service;

namespace VideoNest.Controllers {
    [ApiController]
    [Route("api/[controller]")]
    public class VideosController : ControllerBase {
        private readonly IVideoRepository _videoRepository;
        private readonly ILogger<VideosController> _logger;
        private readonly RabbitMQPublisher _rabbitMQPublisher;

        public VideosController(IVideoRepository videoRepository, ILogger<VideosController> logger, RabbitMQPublisher rabbitMQPublisher) {
            _videoRepository = videoRepository;
            _logger = logger;
            _rabbitMQPublisher = rabbitMQPublisher;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadVideo(IFormFile file) {
            if (file == null || file.Length == 0) {
                _logger.LogWarning("Nenhum arquivo enviado.");
                return BadRequest("Nenhum arquivo enviado.");
            }

            try {
                // Definir caminhos
                var fileName = Path.GetFileName(file.FileName);
                var savePath = Path.Combine("/videos", fileName); // Para Docker
                var hostPath = Path.Combine("C:\\Estudos\\Hackaton_FIAP\\videos", fileName); // Para Windows

                // Criar diretório
                var directory = Path.GetDirectoryName(hostPath);
                if (!string.IsNullOrEmpty(directory)) {
                    Directory.CreateDirectory(directory);
                }

                // Salvar o arquivo
                using (var stream = new FileStream(hostPath, FileMode.Create)) {
                    await file.CopyToAsync(stream);
                }
                _logger.LogInformation("Vídeo salvo em {HostPath}", hostPath);

                // Criar registro no banco
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

                // Enviar mensagem para o RabbitMQ
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

        [HttpGet("{id}")]
        public async Task<IActionResult> GetVideoStatus(int id) {
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