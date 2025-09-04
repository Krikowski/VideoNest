using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;
using VideoNest.DTO;
using VideoNest.Models;
using VideoNest.Repositories;

namespace VideoNest.Service {
    public class VideoService {
        private readonly IVideoRepository _videoRepository;
        private readonly RabbitMQPublisher _rabbitMQPublisher;
        private readonly ILogger<VideoService> _logger;

        public VideoService(IVideoRepository videoRepository, RabbitMQPublisher rabbitMQPublisher, ILogger<VideoService> logger) {
            _videoRepository = videoRepository;
            _rabbitMQPublisher = rabbitMQPublisher;
            _logger = logger;
        }

        public async Task<int> UploadVideoAsync(VideoUploadRequest request) {
            if (request.File == null || request.File.Length == 0) {
                _logger.LogWarning("Nenhum arquivo enviado.");
                throw new ArgumentException("Nenhum arquivo enviado.");
            }

            try {
                // Definir caminhos
                var fileName = Path.GetFileName(request.File.FileName);
                var savePath = Path.Combine("/videos", fileName); // Para Docker
                var hostPath = Path.Combine("C:\\Estudos\\Hackaton_FIAP\\videos", fileName); // Para Windows

                // Criar diretório
                var directory = Path.GetDirectoryName(hostPath);
                if (!string.IsNullOrEmpty(directory)) {
                    Directory.CreateDirectory(directory);
                }

                // Salvar o arquivo
                using (var stream = new FileStream(hostPath, FileMode.Create)) {
                    await request.File.CopyToAsync(stream);
                }
                _logger.LogInformation("Vídeo salvo em {HostPath}", hostPath);

                // Criar registro no banco
                var video = new VideoDB {
                    Title = request.Title ?? fileName,
                    FilePath = savePath,
                    Status = "Na Fila",
                    Description = request.Description ?? "Vídeo enviado",
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

                return video.Id;
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao processar upload do vídeo.");
                throw;
            }
        }

        public async Task<VideoDB?> GetVideoByIdAsync(int id) {
            return await _videoRepository.GetVideoByIdAsync(id);
        }
    }
}