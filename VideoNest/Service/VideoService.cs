using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions; 
using System.Threading.Tasks;
using VideoNest.DTO;
using VideoNest.Hubs;
using VideoNest.Models;
using VideoNest.Repositories;
using VideoNest.Services;
using Prometheus; 

namespace VideoNest.Services {
    /// <summary>
    /// Serviço principal para gerenciamento de vídeos 
    /// Orquestra: upload, persistência MongoDB, publicação RabbitMQ, cache Redis, SignalR
    /// </summary>
    /// <remarks>
    /// Cache Redis otimização.
    /// SignalR notificações real-time (bônus).
    /// </remarks>
    public class VideoService : IVideoService, IDisposable {
        #region Constantes e Configurações (Clean Code)

        /// <summary>
        /// Constantes de limite e configurações
        /// </summary>
        private static class Limits {
            public const long MaxFileSizeBytes = 100 * 1024 * 1024; 
            public const int CacheTtlMinutes = 15;
            public const int RetryMaxAttempts = 3;
            public const int BufferSizeBytes = 81920; 
            public const int CacheExpirationWarningMinutes = 14;
        }

        /// <summary>
        /// Status válidos do processamento
        /// </summary>
        private static class VideoStatuses {
            public const string Queued = "Na Fila";
            public const string Processing = "Processando";
            public const string Completed = "Concluído";
            public const string Error = "Erro";
        }

        #endregion

        #region Dependências

        private readonly IVideoRepository _videoRepository;
        private readonly ILogger<VideoService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IRabbitMQPublisher _rabbitPublisher;
        private readonly IHubContext<VideoHub> _hubContext;
        private readonly IDistributedCache _cache;

        #endregion

        #region Métricas Prometheus 

        // Métrica que estava faltando
        private static readonly Counter QRCodesDetected = Metrics.CreateCounter(
            "videonest_qrcodes_detected_total",
            "Total de QR Codes detectados nos vídeos");

        private static readonly Counter UploadsTotal = Metrics.CreateCounter(
            "videonest_uploads_total",
            "Total de uploads processados");

        private static readonly Histogram UploadDuration = Metrics.CreateHistogram(
            "videonest_upload_duration_seconds",
            "Duração dos uploads");

        private static readonly Counter TestMessagesSent = Metrics.CreateCounter(
            "videonest_test_messages_sent",
            "Mensagens de teste enviadas");

        #endregion

        #region Configurações

        private readonly string _videoBasePath;
        private readonly string _queueName;

        #endregion

        /// <summary>
        /// Inicializa o serviço com todas as dependências
        /// </summary>
        public VideoService(
            IVideoRepository videoRepository,
            ILogger<VideoService> logger,
            IConfiguration configuration,
            IRabbitMQPublisher rabbitPublisher,
            IHubContext<VideoHub> hubContext,
            IDistributedCache cache) {
            _videoRepository = videoRepository ?? throw new ArgumentNullException(nameof(videoRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _rabbitPublisher = rabbitPublisher ?? throw new ArgumentNullException(nameof(rabbitPublisher));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));

            _videoBasePath = configuration["VideoStorage:BasePath"] ?? "/uploads";
            _queueName = configuration["RabbitMQ:QueueName"] ?? "video_queue";

            InitializeUploadDirectory();
        }

        #region FASE 02: Upload de Vídeo (Refatorado - Clean Code)

        /// <summary>
        /// Upload simplificado com orquestrador (SRP)
        /// Fluxo: Validação → Salva arquivo → MongoDB → RabbitMQ → SignalR → Cache Redis
        /// Upload via API + fila assíncrona
        /// </summary>
        /// <param name="file">Arquivo .mp4/.avi (máx 100MB)</param>
        /// <param name="request">Metadados (Title, Description)</param>
        /// <returns>ID sequencial do vídeo</returns>
        /// <exception cref="ArgumentException">Arquivo inválido</exception>
        /// <exception cref="InvalidOperationException">Erro MongoDB/RabbitMQ</exception>
        public async Task<int> UploadVideoAsync(IFormFile file, VideoUploadRequest request) {
            var stopwatch = Stopwatch.StartNew();
            int videoId = 0; 

            try {
                _logger.LogInformation("Iniciando upload: {Title} ({Size} bytes, {ContentType})",
                    GetVideoTitle(request, file), file.Length, file.ContentType);

                videoId = await new VideoUploadOrchestrator(this).ProcessAsync(file, request);

                UploadsTotal.Inc(); 
                _logger.LogInformation("✅ Upload concluído: VideoId={Id}", videoId);

                return videoId;
            } catch (ArgumentException ex) {
                _logger.LogWarning(ex, "Validação falhou: {Title}", GetVideoTitle(request, file));
                throw;
            } catch (IOException ex) {
                _logger.LogError(ex, "I/O erro no upload: {Title}", GetVideoTitle(request, file));
                throw new InvalidOperationException("Erro ao salvar vídeo", ex);
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro inesperado no upload: {Title}", GetVideoTitle(request, file));
                throw new InvalidOperationException("Erro interno no upload", ex);
            } finally {
                stopwatch.Stop();
                UploadDuration.Observe(stopwatch.Elapsed.TotalSeconds);
                _logger.LogDebug("Upload finalizado {ElapsedMs}ms: VideoId={Id}",
                    stopwatch.ElapsedMilliseconds, videoId);
            }
        }

        #endregion

        #region FASE 01: Teste de Fila RabbitMQ

        /// <summary>
        /// Publica mensagem de teste na fila RabbitMQ
        /// Endpoint: POST /api/videos/testqueue
        /// </summary>
        /// <param name="message">Mensagem de teste (ex: "Hello World")</param>
        /// <exception cref="ArgumentException">Mensagem vazia</exception>
        public void PublishTestMessage(string message) // ✅ REMOVIDA: Duplicata mantida apenas esta
        {
            if (string.IsNullOrWhiteSpace(message)) {
                throw new ArgumentException("Mensagem de teste obrigatória", nameof(message));
            }

            try {
                _rabbitPublisher.PublishMessage(message);
                TestMessagesSent.Inc();
                _logger.LogInformation("🧪 Teste publicado: {Message} → {Queue}", message, _queueName);
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro teste RabbitMQ: {Message}", message);
                throw new InvalidOperationException("Falha ao testar fila", ex);
            }
        }

        #endregion

        #region FASE 02-03: Consultas com Cache

        /// <summary>
        /// Consulta vídeo por ID com cache Redis
        /// Retorna status atual + metadados
        /// Cache Redis: 15min TTL, Sliding 5min
        /// </summary>
        /// <param name="id">ID do vídeo</param>
        /// <returns>VideoResult ou null (404)</returns>
        public async Task<VideoResult?> BuscaVideoPorIDService(int id) {
            if (id <= 0) {
                _logger.LogWarning("ID inválido: {VideoId}", id);
                return null;
            }

            try {
                var cached = await GetStatusCacheAsync(id);
                if (cached != null) {
                    _logger.LogDebug("Cache hit Redis: VideoId={Id}, Status={Status}", id, cached.Status);
                    return cached;
                }

                var video = await _videoRepository.GetVideoByIdAsync(id);
                if (video == null) {
                    _logger.LogWarning("Não encontrado MongoDB: VideoId={Id}", id);
                    return null;
                }

                await SetStatusCacheAsync(id, video.Status, video.Duration);
                _logger.LogDebug("Carregado MongoDB: VideoId={Id}, Status={Status}", id, video.Status);

                return video;
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro consulta VideoId={Id}", id);
                throw new InvalidOperationException($"Erro ao consultar vídeo {id}", ex);
            }
        }

        #endregion

        #region FASE 03: Atualizações de Status e QR Codes

        /// <summary>
        /// Atualiza status (ScanForge → API)
        /// Invalida cache Redis, notifica SignalR
        /// </summary>
        public async Task UpdateVideoStatusAsync(int videoId, string status, string? errorMessage = null, int duration = 0) {
            if (videoId <= 0) throw new ArgumentException("ID inválido", nameof(videoId));
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status vazio", nameof(status));

            try {
                await _videoRepository.UpdateStatusAsync(videoId, status, errorMessage, duration);
                
                await InvalidateStatusCacheAsync(videoId);

                _logger.LogInformation("Status atualizado: VideoId={Id}, Status={Status}, Duration={Duration}s",
                    videoId, status, duration);

                await NotifyStatusUpdateAsync(videoId, status);
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro update status VideoId={Id}", videoId);
                throw new InvalidOperationException($"Erro status vídeo {videoId}", ex);
            }
        }

        /// <summary>
        /// Adiciona QR Codes (ScanForge → API)
        /// Armazena array QRCodes no MongoDB
        /// Invalida cache, notifica SignalR
        /// </summary>
        public async Task AddQRCodesToVideoAsync(int videoId, List<QRCodeResult> qrCodes) {
            if (videoId <= 0) throw new ArgumentException("ID inválido", nameof(videoId));
            if (qrCodes == null || !qrCodes.Any()) {
                _logger.LogDebug("Nenhum QR Code: VideoId={Id}", videoId);
                return;
            }

            try {
                ValidateQRCodes(qrCodes);

                await _videoRepository.AddQRCodesAsync(videoId, qrCodes);

                // Métrica agora definida
                QRCodesDetected.Inc(qrCodes.Count);

                // Invalida cache
                await InvalidateStatusCacheAsync(videoId);

                _logger.LogInformation("QR Codes adicionados: VideoId={Id}, Count={Count}", videoId, qrCodes.Count);

                // SignalR
                await NotifyQRCodesDetectedAsync(videoId, qrCodes.Count);
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro QR Codes VideoId={Id}", videoId);
                throw new InvalidOperationException($"Erro QR Codes vídeo {videoId}", ex);
            }
        }

        #endregion

        #region FASE 04: Notificações SignalR (Centralizadas - Clean Code)

        /// <summary>
        /// Notificação genérica SignalR
        /// </summary>
        private async Task NotifySignalRAsync(string eventName, object payload, int videoId) {
            try {
                await _hubContext.Clients.All.SendAsync(eventName, payload);
                _logger.LogDebug("SignalR {Event}: VideoId={Id}", eventName, videoId);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Erro SignalR {Event}: VideoId={Id}", eventName, videoId);
                // Não falha o processamento principal
            }
        }

        /// <summary>
        /// Notifica vídeo na fila (bônus)
        /// </summary>
        public async Task NotifyVideoQueuedAsync(int videoId, string title) {
            var payload = new {
                VideoId = videoId,
                Title = title,
                Status = VideoStatuses.Queued,
                Timestamp = DateTime.UtcNow
            };
            await NotifySignalRAsync("VideoQueued", payload, videoId);
        }

        /// <summary>
        /// Notifica status atualizado (bônus)
        /// </summary>
        private async Task NotifyStatusUpdateAsync(int videoId, string status) {
            var payload = new {
                VideoId = videoId,
                Status = status,
                Timestamp = DateTime.UtcNow
            };
            await NotifySignalRAsync("StatusUpdated", payload, videoId);
        }

        /// <summary>
        /// Notifica QR Codes detectados (bônus)
        /// </summary>
        private async Task NotifyQRCodesDetectedAsync(int videoId, int qrCount) {
            var payload = new {
                VideoId = videoId,
                QrCount = qrCount,
                Timestamp = DateTime.UtcNow
            };
            await NotifySignalRAsync("QRCodesDetected", payload, videoId);
        }

        /// <summary>
        /// Notifica conclusão via SignalR (bônus)
        /// </summary>
        public async Task NotifyVideoCompletedAsync(int videoId, List<QRCodeResult>? qrCodes = null) {
            var payload = new {
                VideoId = videoId,
                Status = VideoStatuses.Completed,
                QrCodes = qrCodes ?? new List<QRCodeResult>(),
                Timestamp = DateTime.UtcNow
            };
            await NotifySignalRAsync("VideoProcessed", payload, videoId);
        }

        #endregion

        #region Métodos Privados - Validações e Helpers

        /// <summary>
        /// Validador específico (SRP)
        /// Valida arquivo de vídeo (.mp4/.avi, 100MB max)
        /// </summary>
        private void ValidateUploadRequest(IFormFile file, VideoUploadRequest request) {
            if (file == null || file.Length == 0)
                throw new ArgumentException("Arquivo obrigatório", nameof(file));

            var allowedExtensions = new[] { ".mp4", ".avi" };
            var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
                throw new ArgumentException($"Formato inválido. Aceitos: {string.Join(", ", allowedExtensions)}", nameof(file));

            if (file.Length > Limits.MaxFileSizeBytes)
                throw new ArgumentException($"Máximo {Limits.MaxFileSizeBytes / (1024 * 1024)}MB", nameof(file));
        }

        /// <summary>
        /// Regex importado e usado
        /// Gera nome único: "demo_20250115T103012345.mp4"
        /// </summary>
        private static string GenerateUniqueFileName(string originalFileName) {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var extension = Path.GetExtension(originalFileName);
            var name = Path.GetFileNameWithoutExtension(originalFileName);

            // Regex para caracteres seguros (Linux-safe)
            name = Regex.Replace(name, @"[^\w\-_. ]", "_");

            return $"{name}_{timestamp}{extension}";
        }

        /// <summary>
        /// Método específico para salvar arquivo
        /// Salva arquivo com stream async (80KB buffer)
        /// </summary>
        private async Task<string> SaveVideoFileAsync(IFormFile file) {
            var uniqueFileName = GenerateUniqueFileName(file.FileName);
            var savePath = Path.Combine(_videoBasePath, uniqueFileName).Replace("\\", "/");

            var directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write,
                FileShare.None, bufferSize: Limits.BufferSizeBytes, useAsync: true);
            using var inputStream = file.OpenReadStream();
            var buffer = new byte[Limits.BufferSizeBytes];
            int bytesRead;

            while ((bytesRead = await inputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                await fileStream.WriteAsync(buffer, 0, bytesRead);

            await fileStream.FlushAsync();

            var savedFile = new FileInfo(savePath);
            if (savedFile.Length != file.Length)
                throw new InvalidOperationException(
                    $"Tamanho salvo ({savedFile.Length}) ≠ original ({file.Length})");

            return savePath;
        }

        /// <summary>
        /// Criação de entidade isolada
        /// Cria VideoResult inicial
        /// </summary>
        private static VideoResult CreateVideoEntity(VideoUploadRequest request, string filePath) {
            return new VideoResult {
                Title = string.IsNullOrWhiteSpace(request.Title)
                    ? Path.GetFileNameWithoutExtension(filePath)
                    : request.Title,
                Description = request.Description ?? "Vídeo enviado via API",
                FilePath = filePath,
                Status = VideoStatuses.Queued,
                CreatedAt = DateTime.UtcNow,
                Duration = 0,
                QRCodes = new List<QRCodeResult>(),
                ErrorMessage = null
            };
        }

        /// <summary>
        /// Publicação com retry isolada
        /// Publica no RabbitMQ com retry exponencial
        /// </summary>
        private async Task PublishVideoMessageAsync(int videoId, string filePath) {
            const int maxRetries = Limits.RetryMaxAttempts;
            var retryCount = 0;

            while (retryCount < maxRetries) {
                try {
                    var message = new {
                        VideoId = videoId,
                        FilePath = filePath,
                        Timestamp = DateTime.UtcNow
                    };

                    var json = JsonConvert.SerializeObject(message, new JsonSerializerSettings {
                        NullValueHandling = NullValueHandling.Ignore,
                        Formatting = Formatting.None
                    });

                    _rabbitPublisher.PublishMessage(json);
                    _logger.LogInformation("📤 Publicado RabbitMQ: VideoId={Id}, Path={Path}", videoId, filePath);
                    return;
                } catch (Exception ex) when (retryCount < maxRetries - 1) {
                    retryCount++;
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                    _logger.LogWarning(ex, "Retry {Count}/{Max} em {Delay}s: VideoId={Id}",
                        retryCount, maxRetries, delay.TotalSeconds, videoId);
                    await Task.Delay(delay);
                }
            }

            _logger.LogError("Falha após {MaxRetries} tentativas: VideoId={Id}", maxRetries, videoId);
            throw new InvalidOperationException($"Falha RabbitMQ após {maxRetries} tentativas");
        }

        /// <summary>
        /// Título para logging (helper)
        /// </summary>
        private static string GetVideoTitle(VideoUploadRequest request, IFormFile file) {
            return request?.Title ?? Path.GetFileNameWithoutExtension(file.FileName) ?? "Vídeo sem título";
        }

        /// <summary>
        /// Valida QR Codes (timestamps >= 0)
        /// </summary>
        private static void ValidateQRCodes(List<QRCodeResult> qrCodes) {
            foreach (var qr in qrCodes) {
                if (qr.Timestamp < 0)
                    throw new ArgumentException($"Timestamp inválido: {qr.Timestamp}");
            }
        }

        #endregion

        #region Cache Redis (FASE 03)

        /// <summary>
        /// Métodos de cache isolados
        /// Cacheia status Redis
        /// </summary>
        private async Task SetStatusCacheAsync(int videoId, string status, int duration) {
            try {
                var key = $"video_status_{videoId}";
                var value = JsonConvert.SerializeObject(new {
                    Status = status,
                    Duration = duration,
                    CachedAt = DateTime.UtcNow
                });

                var options = new DistributedCacheEntryOptions {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Limits.CacheTtlMinutes)
                };

                await _cache.SetStringAsync(key, value, options);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Erro cache Redis: VideoId={Id}", videoId);
            }
        }

        /// <summary>
        /// Recupera status do cache Redis
        /// </summary>
        private async Task<VideoResult?> GetStatusCacheAsync(int videoId) {
            try {
                var key = $"video_status_{videoId}";
                var value = await _cache.GetStringAsync(key);

                if (string.IsNullOrEmpty(value)) return null;

                var data = JsonConvert.DeserializeObject<dynamic>(value);

                if (data.CachedAt != null) {
                    var cachedAt = DateTime.Parse(data.CachedAt.ToString());
                    if (DateTime.UtcNow - cachedAt > TimeSpan.FromMinutes(Limits.CacheExpirationWarningMinutes)) {
                        _logger.LogDebug("Cache expirado, removendo: VideoId={VideoId}", videoId);
                        await _cache.RemoveAsync(key);
                        return null;
                    }
                }

                return new VideoResult {
                    VideoId = videoId,
                    Status = data.Status.ToString() ?? "Desconhecido",
                    Duration = (int?)data.Duration ?? 0
                };
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Erro cache get: VideoId={Id}", videoId);
                return null;
            }
        }

        /// <summary>
        /// Invalida cache de status
        /// </summary>
        private async Task InvalidateStatusCacheAsync(int videoId) {
            try {
                var key = $"video_status_{videoId}";
                await _cache.RemoveAsync(key);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Erro invalidar cache: VideoId={Id}", videoId);
            }
        }

        #endregion

        #region Inicialização

        /// <summary>
        /// Inicializa diretório de uploads
        /// </summary>
        private void InitializeUploadDirectory() {
            try {
                if (!Directory.Exists(_videoBasePath)) {
                    Directory.CreateDirectory(_videoBasePath);
                    _logger.LogInformation("Diretório de uploads criado: {VideoBasePath}", _videoBasePath);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao criar diretório {VideoBasePath}", _videoBasePath);
                throw new InvalidOperationException($"Falha ao configurar armazenamento: {ex.Message}", ex);
            }
        }

        #endregion

        #region Orquestrador de Upload (Clean Code - SRP)

        /// <summary>
        /// Classe interna para orquestração de upload 
        /// Separa responsabilidades do método principal
        /// </summary>
        private class VideoUploadOrchestrator {
            private readonly VideoService _service;

            public VideoUploadOrchestrator(VideoService service) {
                _service = service;
            }

            public async Task<int> ProcessAsync(IFormFile file, VideoUploadRequest request) {
                _service.ValidateUploadRequest(file, request);

                var savePath = await _service.SaveVideoFileAsync(file);

                var video = VideoService.CreateVideoEntity(request, savePath);
                var videoId = await _service._videoRepository.GetNextIdAsync();
                video.VideoId = videoId;

                await _service._videoRepository.SaveVideoAsync(video);

                await _service.SetStatusCacheAsync(videoId, video.Status, 0);

                await _service.PublishVideoMessageAsync(videoId, savePath);

                await _service.NotifyVideoQueuedAsync(videoId, video.Title);

                return videoId;
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Limpeza de recursos
        /// </summary>
        public void Dispose() {
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}