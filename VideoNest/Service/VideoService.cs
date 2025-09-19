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
using System.Threading.Tasks;
using VideoNest.DTO;
using VideoNest.Hubs;
using VideoNest.Models;
using VideoNest.Repositories;
using VideoNest.Services;
using Prometheus;

namespace VideoNest.Services {
    /// <summary>
    /// Serviço principal para gerenciamento de vídeos (FASE 01-04)
    /// Responsável por: upload, persistência, publicação em fila, notificações SignalR
    /// </summary>
    public class VideoService : IVideoService, IDisposable {
        private readonly IVideoRepository _videoRepository;
        private readonly ILogger<VideoService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IRabbitMQPublisher _rabbitPublisher;
        private readonly IHubContext<VideoHub> _hubContext;
        private readonly IDistributedCache _cache; // Redis para cache de status (FASE 03)

        // Métricas Prometheus (BONUS)
        private static readonly Counter UploadsTotal = Metrics.CreateCounter("videonest_uploads_total", "Total de uploads processados");
        private static readonly Counter QRCodesDetected = Metrics.CreateCounter("videonest_qrcodes_detected_total", "Total de QR Codes detectados");
        private static readonly Histogram UploadDuration = Metrics.CreateHistogram("videonest_upload_duration_seconds", "Duração dos uploads");
        private static readonly Counter TestMessagesSent = Metrics.CreateCounter("videonest_test_messages_sent", "Mensagens de teste enviadas");

        private readonly string _videoBasePath;
        private readonly string _queueName;

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

            // Garantir que diretório de uploads existe
            try {
                if (!Directory.Exists(_videoBasePath)) {
                    Directory.CreateDirectory(_videoBasePath);
                }
                _logger.LogInformation("Diretório de uploads configurado: {VideoBasePath}", _videoBasePath);
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao criar diretório de uploads: {VideoBasePath}", _videoBasePath);
                throw new InvalidOperationException($"Falha ao configurar armazenamento: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// FASE 02: Upload de vídeo com validação, persistência e publicação na fila
        /// </summary>
        /// <param name="file">Arquivo de vídeo (.mp4/.avi)</param>
        /// <param name="request">Metadados do vídeo</param>
        /// <returns>ID do vídeo processado</returns>
        public async Task<int> UploadVideoAsync(IFormFile file, VideoUploadRequest request) {
            var stopwatch = Stopwatch.StartNew();
            var videoId = 0;
            string savePath = string.Empty; // Declarada aqui para scope

            try {
                // Validação de entrada (FASE 02)
                ValidateUploadRequest(file, request);

                _logger.LogInformation("Iniciando upload: {Title} ({FileSize} bytes, {ContentType})",
                    GetVideoTitle(request, file),
                    file.Length,
                    file.ContentType);

                // Gerar nome único para arquivo (evita conflitos)
                var uniqueFileName = GenerateUniqueFileName(file.FileName);
                savePath = Path.Combine(_videoBasePath, uniqueFileName).Replace("\\", "/");

                // Salvar arquivo no disco (FASE 02)
                await SaveVideoFileAsync(file, savePath);

                // Gerar ID único e persistir metadados (MongoDB - FASE 03)
                var video = CreateVideoEntity(request, savePath);
                videoId = await _videoRepository.GetNextIdAsync();
                video.VideoId = videoId;

                await _videoRepository.SaveVideoAsync(video);

                // Cache inicial no Redis (FASE 03 - otimização consultas)
                await SetStatusCacheAsync(videoId, video.Status, video.Duration);

                // Publicar mensagem na fila RabbitMQ (FASE 01-02)
                await PublishVideoMessageAsync(videoId, savePath);

                // Notificar clientes via SignalR (FASE 04 - BONUS)
                await NotifyVideoQueuedAsync(videoId, video.Title);

                _logger.LogInformation("Upload concluído com sucesso: VideoId={VideoId}, Path={SavePath}",
                    videoId, savePath);

                UploadsTotal.Inc();
                return videoId;

            } catch (ArgumentException ex) {
                _logger.LogWarning(ex, "Erro de validação no upload: {Title}", GetVideoTitle(request, file));
                throw;
            } catch (IOException ex) {
                _logger.LogError(ex, "Erro de I/O ao salvar vídeo: {SavePath}", savePath);
                throw new InvalidOperationException("Erro ao salvar arquivo de vídeo", ex);
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro inesperado no upload: {Title}", GetVideoTitle(request, file));
                throw new InvalidOperationException("Erro interno ao processar upload", ex);
            } finally {
                stopwatch.Stop();
                UploadDuration.Observe(stopwatch.Elapsed.TotalSeconds);
                _logger.LogDebug("Upload finalizado em {ElapsedMs}ms: VideoId={VideoId}",
                    stopwatch.ElapsedMilliseconds, videoId);
            }
        }

        /// <summary>
        /// FASE 01: Endpoint de teste para fila RabbitMQ
        /// </summary>
        public void PublishTestMessage(string message) {
            if (string.IsNullOrWhiteSpace(message)) {
                _logger.LogWarning("Mensagem de teste vazia");
                throw new ArgumentException("Mensagem de teste não pode ser vazia", nameof(message));
            }

            try {
                _rabbitPublisher.PublishMessage(message);
                TestMessagesSent.Inc();
                _logger.LogInformation("✅ Mensagem de teste enviada para fila: {Message}", message);
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao enviar mensagem de teste para fila");
                throw new InvalidOperationException("Falha ao testar fila de mensagens", ex);
            }
        }

        /// <summary>
        /// FASE 02-03: Consulta de vídeo por ID com cache Redis
        /// </summary>
        public async Task<VideoResult?> BuscaVideoPorIDService(int id) {
            if (id <= 0) {
                _logger.LogWarning("ID inválido para consulta: {VideoId}", id);
                return null;
            }

            try {
                // Tentar cache Redis primeiro (FASE 03 - otimização)
                var cachedStatus = await GetStatusCacheAsync(id);
                if (cachedStatus != null) {
                    _logger.LogDebug("Status recuperado do cache Redis: VideoId={VideoId}, Status={Status}", id, cachedStatus.Status);
                    return cachedStatus;
                }

                // Buscar do MongoDB
                var video = await _videoRepository.GetVideoByIdAsync(id);
                if (video == null) {
                    _logger.LogWarning("Vídeo não encontrado no MongoDB: VideoId={VideoId}", id);
                    return null;
                }

                // Cachear resultado
                await SetStatusCacheAsync(id, video.Status, video.Duration);

                _logger.LogDebug("Vídeo carregado do MongoDB: VideoId={VideoId}, Status={Status}", id, video.Status);
                return video;

            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao buscar vídeo ID {VideoId}", id);
                throw new InvalidOperationException($"Erro ao consultar vídeo {id}", ex);
            }
        }

        /// <summary>
        /// FASE 03: Atualizar status do vídeo (chamado pelo ScanForge)
        /// </summary>
        public async Task UpdateVideoStatusAsync(int videoId, string status, string? errorMessage = null, int duration = 0) {
            if (videoId <= 0) {
                throw new ArgumentException("ID do vídeo deve ser maior que zero", nameof(videoId));
            }

            if (string.IsNullOrWhiteSpace(status)) {
                throw new ArgumentException("Status não pode ser vazio", nameof(status));
            }

            try {
                // Atualizar MongoDB
                await _videoRepository.UpdateStatusAsync(videoId, status, errorMessage, duration);

                // Atualizar cache Redis
                await SetStatusCacheAsync(videoId, status, duration);

                _logger.LogInformation("Status atualizado: VideoId={VideoId}, Status={Status}, Duration={Duration}s",
                    videoId, status, duration);

                // Notificar via SignalR (FASE 04)
                await NotifyStatusUpdateAsync(videoId, status);

                // Métricas (BONUS)
                if (status == "Concluído") {
                    // Aqui seria chamado após detecção de QRs no ScanForge
                    // QRCodesDetected.Inc(qrCount);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao atualizar status do vídeo ID {VideoId}", videoId);
                throw new InvalidOperationException($"Erro ao atualizar status do vídeo {videoId}", ex);
            }
        }

        /// <summary>
        /// FASE 03: Adicionar resultados de QR Codes (chamado pelo ScanForge)
        /// </summary>
        public async Task AddQRCodesToVideoAsync(int videoId, List<QRCodeResult> qrCodes) {
            if (videoId <= 0) {
                throw new ArgumentException("ID do vídeo deve ser maior que zero", nameof(videoId));
            }

            if (qrCodes == null || qrCodes.Count == 0) {
                _logger.LogDebug("Nenhum QR Code para adicionar: VideoId={VideoId}", videoId);
                return;
            }

            try {
                // Validar QR Codes
                foreach (var qr in qrCodes) {
                    if (qr.Timestamp < 0) {
                        throw new ArgumentException($"Timestamp inválido para QR Code: {qr.Timestamp}", nameof(qrCodes));
                    }
                }

                await _videoRepository.AddQRCodesAsync(videoId, qrCodes);

                // Atualizar métricas (BONUS)
                QRCodesDetected.Inc(qrCodes.Count);

                _logger.LogInformation("QR Codes adicionados: VideoId={VideoId}, Count={Count}", videoId, qrCodes.Count);

                // Notificar via SignalR (FASE 04)
                await NotifyQRCodesDetectedAsync(videoId, qrCodes.Count);
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao adicionar QR Codes ao vídeo ID {VideoId}", videoId);
                throw new InvalidOperationException($"Erro ao salvar QR Codes do vídeo {videoId}", ex);
            }
        }

        /// <summary>
        /// FASE 04: Notificação SignalR de conclusão de processamento
        /// </summary>
        public async Task NotifyVideoCompletedAsync(int videoId, List<QRCodeResult>? qrCodes = null) {
            try {
                await _hubContext.Clients.All.SendAsync("VideoProcessed", new {
                    VideoId = videoId,
                    Status = "Concluído",
                    QrCodes = qrCodes ?? new List<QRCodeResult>(),
                    Timestamp = DateTime.UtcNow,
                    Message = "Processamento concluído com sucesso"
                });

                _logger.LogInformation("Notificação SignalR enviada: VideoProcessed, VideoId={VideoId}, QrCount={QrCount}",
                    videoId, qrCodes?.Count ?? 0);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Erro ao notificar via SignalR: VideoProcessed, VideoId={VideoId}", videoId);
                // Não falha o processo principal
            }
        }

        #region Métodos Privados Auxiliares

        /// <summary>
        /// Valida se é um arquivo de vídeo suportado (FASE 02)
        /// </summary>
        private void ValidateUploadRequest(IFormFile file, VideoUploadRequest request) {
            if (file == null || file.Length == 0) {
                throw new ArgumentException("Arquivo de vídeo é obrigatório", nameof(file));
            }

            var allowedExtensions = new[] { ".mp4", ".avi", ".mov", ".mkv" };
            var allowedContentTypes = new[] { "video/mp4", "video/x-msvideo", "video/quicktime", "video/x-matroska" };

            var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            var contentType = file.ContentType?.ToLowerInvariant();

            var isValidExtension = !string.IsNullOrEmpty(extension) && allowedExtensions.Contains(extension);
            var isValidContentType = !string.IsNullOrEmpty(contentType) && allowedContentTypes.Contains(contentType);

            if (!isValidExtension && !isValidContentType) {
                throw new ArgumentException("Formato inválido. Aceitos: .mp4, .avi, .mov, .mkv", nameof(file));
            }

            var maxFileSize = GetMaxFileSize();
            if (file.Length > maxFileSize) {
                throw new ArgumentException($"Arquivo muito grande. Máximo: {maxFileSize / (1024 * 1024)}MB", nameof(file));
            }
        }

        /// <summary>
        /// Gera nome único para arquivo (evita sobreposição)
        /// </summary>
        private static string GenerateUniqueFileName(string originalFileName) {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var extension = Path.GetExtension(originalFileName);
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);

            // Sanitizar nome (Linux-friendly)
            nameWithoutExtension = System.Text.RegularExpressions.Regex.Replace(nameWithoutExtension, @"[^\w\-_. ]", "_");

            return $"{nameWithoutExtension}_{timestamp}{extension}";
        }

        /// <summary>
        /// Salva arquivo de vídeo no disco (compatível Linux)
        /// </summary>
        private async Task SaveVideoFileAsync(IFormFile file, string savePath) {
            try {
                // Garantir que diretório pai existe
                var directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }

                // Salvar com encoding UTF-8 e buffer otimizado
                using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
                using var inputStream = file.OpenReadStream();

                var buffer = new byte[81920]; // 80KB buffer
                int bytesRead;
                while ((bytesRead = await inputStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                }

                await fileStream.FlushAsync();

                // Verificar se arquivo foi salvo corretamente
                var savedFileInfo = new FileInfo(savePath);
                if (savedFileInfo.Length != file.Length) {
                    throw new InvalidOperationException($"Tamanho do arquivo salvo ({savedFileInfo.Length}) não corresponde ao original ({file.Length})");
                }

                _logger.LogDebug("Arquivo salvo com sucesso: {SavePath} ({FileSize} bytes)", savePath, savedFileInfo.Length);
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao salvar arquivo: {SavePath}", savePath);
                // Limpar arquivo parcial se existir
                if (File.Exists(savePath)) {
                    try { File.Delete(savePath); } catch { /* Ignorar erro de limpeza */ }
                }
                throw;
            }
        }

        /// <summary>
        /// Cria entidade VideoResult a partir do request
        /// </summary>
        private static VideoResult CreateVideoEntity(VideoUploadRequest request, string filePath) {
            return new VideoResult {
                Title = string.IsNullOrWhiteSpace(request.Title) ? Path.GetFileNameWithoutExtension(filePath) : request.Title,
                Description = request.Description ?? "Vídeo enviado via API",
                FilePath = filePath,
                Status = "Na Fila",
                CreatedAt = DateTime.UtcNow,
                Duration = 0, // Será preenchido pelo ScanForge
                QRCodes = new List<QRCodeResult>(),
                ErrorMessage = null
            };
        }

        /// <summary>
        /// Publica mensagem na fila RabbitMQ com retry (FASE 01-02)
        /// </summary>
        private async Task PublishVideoMessageAsync(int videoId, string filePath) {
            const int maxRetries = 3;
            var retryCount = 0;

            while (retryCount < maxRetries) {
                try {
                    var message = new {
                        VideoId = videoId,
                        FilePath = filePath,
                        Timestamp = DateTime.UtcNow,
                        Priority = 1 // Para ordenação na fila
                    };

                    var jsonMessage = JsonConvert.SerializeObject(message, new JsonSerializerSettings {
                        DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffZ",
                        NullValueHandling = NullValueHandling.Ignore,
                        Formatting = Formatting.None // Compact JSON para performance
                    });

                    _rabbitPublisher.PublishMessage(jsonMessage);

                    _logger.LogInformation("📤 Mensagem publicada na fila: VideoId={VideoId}, Path={FilePath}, Queue={QueueName}",
                        videoId, filePath, _queueName);

                    return; // Sucesso
                } catch (Exception ex) when (retryCount < maxRetries - 1) {
                    retryCount++;
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount)); // Backoff exponencial
                    _logger.LogWarning(ex, "Tentativa {RetryCount}/{MaxRetries} falhou. Aguardando {Delay}s: VideoId={VideoId}",
                        retryCount, maxRetries, delay.TotalSeconds, videoId);

                    await Task.Delay(delay);
                }
            }

            // Falha final
            _logger.LogError("Falha ao publicar mensagem após {MaxRetries} tentativas: VideoId={VideoId}", maxRetries, videoId);
            throw new InvalidOperationException($"Não foi possível publicar mensagem na fila após {maxRetries} tentativas");
        }

        /// <summary>
        /// Obtém tamanho máximo de arquivo da configuração
        /// </summary>
        private long GetMaxFileSize() {
            var maxSizeMb = _configuration.GetValue<int>("VideoStorage:MaxFileSizeMb", 100);
            return maxSizeMb * 1024L * 1024L; // Converter para bytes (long para arquivos grandes)
        }

        /// <summary>
        /// Obtém título do vídeo para logging
        /// </summary>
        private static string GetVideoTitle(VideoUploadRequest request, IFormFile file) {
            return request?.Title ?? Path.GetFileNameWithoutExtension(file.FileName) ?? "Vídeo sem título";
        }

        /// <summary>
        /// FASE 04: Notificação SignalR quando vídeo é adicionado à fila
        /// </summary>
        private async Task NotifyVideoQueuedAsync(int videoId, string title) {
            try {
                await _hubContext.Clients.All.SendAsync("VideoQueued", new {
                    VideoId = videoId,
                    Title = title,
                    Status = "Na Fila",
                    Timestamp = DateTime.UtcNow,
                    Message = "Vídeo enviado para processamento assíncrono"
                });

                _logger.LogDebug("Notificação SignalR enviada: VideoQueued, VideoId={VideoId}", videoId);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Erro ao notificar via SignalR: VideoQueued, VideoId={VideoId}", videoId);
                // Não falha o processo principal
            }
        }

        /// <summary>
        /// FASE 04: Notificação SignalR de atualização de status
        /// </summary>
        private async Task NotifyStatusUpdateAsync(int videoId, string status) {
            try {
                await _hubContext.Clients.All.SendAsync("StatusUpdated", new {
                    VideoId = videoId,
                    Status = status,
                    Timestamp = DateTime.UtcNow
                });

                _logger.LogDebug("Notificação SignalR enviada: StatusUpdated, VideoId={VideoId}, Status={Status}", videoId, status);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Erro ao notificar via SignalR: StatusUpdated, VideoId={VideoId}", videoId);
            }
        }

        /// <summary>
        /// FASE 04: Notificação SignalR de QR Codes detectados
        /// </summary>
        private async Task NotifyQRCodesDetectedAsync(int videoId, int qrCount) {
            try {
                await _hubContext.Clients.All.SendAsync("QRCodesDetected", new {
                    VideoId = videoId,
                    QrCount = qrCount,
                    Timestamp = DateTime.UtcNow,
                    Message = $"{qrCount} QR Code(s) detectado(s)"
                });

                _logger.LogDebug("Notificação SignalR enviada: QRCodesDetected, VideoId={VideoId}, Count={QrCount}", videoId, qrCount);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Erro ao notificar via SignalR: QRCodesDetected, VideoId={VideoId}", videoId);
            }
        }

        /// <summary>
        /// FASE 03: Cache Redis para status (otimização consultas)
        /// </summary>
        private async Task SetStatusCacheAsync(int videoId, string status, int duration) {
            try {
                var cacheKey = $"video_status_{videoId}";
                var cacheValue = JsonConvert.SerializeObject(new {
                    Status = status,
                    Duration = duration,
                    CachedAt = DateTime.UtcNow
                });

                var cacheOptions = new DistributedCacheEntryOptions {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15), // 15min TTL
                    SlidingExpiration = TimeSpan.FromMinutes(5)
                };

                await _cache.SetStringAsync(cacheKey, cacheValue, cacheOptions);
                _logger.LogDebug("Status cacheado no Redis: VideoId={VideoId}, Status={Status}", videoId, status);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Erro ao cachear status no Redis: VideoId={VideoId}", videoId);
                // Não falha o processo principal
            }
        }

        /// <summary>
        /// FASE 03: Recuperar status do cache Redis
        /// </summary>
        private async Task<VideoResult?> GetStatusCacheAsync(int videoId) {
            try {
                var cacheKey = $"video_status_{videoId}";
                var cachedValue = await _cache.GetStringAsync(cacheKey);

                if (string.IsNullOrEmpty(cachedValue))
                    return null;

                var cachedData = JsonConvert.DeserializeObject<dynamic>(cachedValue);

                // Verificar se cache não expirou (heuristicamente)
                if (cachedData.CachedAt != null) {
                    var cachedAt = DateTime.Parse(cachedData.CachedAt.ToString());
                    if (DateTime.UtcNow - cachedAt > TimeSpan.FromMinutes(14)) // Quase TTL
                    {
                        _logger.LogDebug("Cache expirado, removendo: VideoId={VideoId}", videoId);
                        await _cache.RemoveAsync(cacheKey);
                        return null;
                    }
                }

                return new VideoResult {
                    VideoId = videoId,
                    Status = cachedData.Status.ToString() ?? "Desconhecido",
                    Duration = (int?)cachedData.Duration ?? 0
                };
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Erro ao recuperar cache Redis: VideoId={VideoId}", videoId);
                return null;
            }
        }

        #endregion

        /// <summary>
        /// Limpeza de recursos (FASE 01)
        /// </summary>
        public void Dispose() {
            // O RabbitMQPublisher já implementa IDisposable
            // O cache é gerenciado pelo DI container
            GC.SuppressFinalize(this);
        }
    }
}