using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using VideoNest.DTO;
using VideoNest.Hubs;
using VideoNest.Services;

namespace VideoNest.Controllers {
    /// <summary>
    /// API para upload e consulta de vídeos com detecção de QR Codes
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class VideosController : ControllerBase {
        private readonly IVideoService _videoService;
        private readonly ILogger<VideosController> _logger;
        private readonly IHubContext<VideoHub> _hubContext;

        public VideosController(
            IVideoService videoService,
            ILogger<VideosController> logger,
            IHubContext<VideoHub> hubContext) {
            _videoService = videoService ?? throw new ArgumentNullException(nameof(videoService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        }

        /// <summary>
        /// Upload de vídeo (.mp4, .avi) para processamento assíncrono
        /// </summary>
        /// <param name="request">Arquivo e metadados do vídeo</param>
        /// <returns>ID do vídeo e confirmação</returns>
        /// <response code="200">Upload realizado com sucesso</response>
        /// <response code="400">Arquivo inválido (formato, tamanho)</response>
        /// <response code="500">Erro interno do servidor</response>
        [HttpPost]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        [ProducesResponseType(typeof(object), 500)]
        public async Task<IActionResult> UploadVideo([FromForm] VideoUploadRequest request) {
            if (!ModelState.IsValid) {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .Where(e => e != null)
                    .ToList();

                _logger.LogWarning("Validação automática falhou: {Errors}", string.Join(", ", errors));
                return BadRequest(new { Message = "Validação falhou", Errors = errors });
            }

            if (!request.TryValidate(out var validationError)) {
                _logger.LogWarning("Validação customizada falhou: {Error}", validationError);
                return BadRequest(new { Message = validationError });
            }

            var metadata = request.GetMetadata();
            _logger.LogInformation("Iniciando upload: {Title} ({Size}MB) - {FileName}{Extension}",
                metadata.Title,
                Math.Round((double)metadata.FileSizeBytes / (1024 * 1024), 1),
                metadata.FileName,
                metadata.FileExtension);

            try {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                int videoId = await _videoService.UploadVideoAsync(request.File!, request);

                stopwatch.Stop();

                _logger.LogInformation("Upload concluído com sucesso: VideoId={VideoId} em {ElapsedMs}ms",
                    videoId, stopwatch.ElapsedMilliseconds);

                // 5. Resposta enriquecida com metadados
                return Ok(new {
                    VideoId = videoId,
                    Message = "Vídeo enviado para fila de processamento assíncrono.",
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    FileInfo = new {
                        Name = metadata.FileName,
                        Extension = metadata.FileExtension,
                        SizeMB = Math.Round((double)metadata.FileSizeBytes / (1024 * 1024), 1),
                        Title = metadata.Title
                    }
                });
            } catch (ArgumentException ex) {
                _logger.LogWarning(ex, "Erro de validação no upload: {Title}", request.Title);
                return BadRequest(new { Message = ex.Message });
            } catch (InvalidOperationException ex) {
                _logger.LogError(ex, "Erro operacional no upload: {Title}", request.Title);
                return StatusCode(500, new { Message = "Erro ao processar upload", Details = ex.Message });
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro inesperado no upload: {Title}", request.Title);
                return StatusCode(500, new { Message = "Erro interno do servidor", Details = ex.Message });
            }
        }

        /// <summary>
        /// Consulta status do processamento do vídeo
        /// </summary>
        /// <param name="id">ID do vídeo</param>
        /// <returns>Status atual e metadados</returns>
        /// <response code="200">Status encontrado</response>
        /// <response code="400">ID inválido</response>
        /// <response code="404">Vídeo não encontrado</response>
        /// <response code="500">Erro interno</response>
        [HttpGet("{id}/status")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        [ProducesResponseType(typeof(object), 404)]
        [ProducesResponseType(typeof(object), 500)]
        public async Task<IActionResult> GetVideoStatus(int id) {
            try {
                if (id <= 0) {
                    return BadRequest(new { Message = "ID do vídeo deve ser maior que zero" });
                }

                _logger.LogDebug("Consultando status: VideoId={VideoId}", id);
                var video = await _videoService.BuscaVideoPorIDService(id);

                if (video == null) {
                    _logger.LogWarning("Vídeo não encontrado: VideoId={VideoId}", id);
                    return NotFound(new { Message = "Vídeo não encontrado" });
                }

                var response = new {
                    VideoId = video.VideoId,
                    Title = video.Title,
                    Status = video.Status,
                    Duration = video.Duration,
                    CreatedAt = video.CreatedAt,
                    ErrorMessage = video.Status == "Erro" ? video.ErrorMessage : null,
                    LastUpdated = DateTime.UtcNow
                };

                _logger.LogDebug("Status retornado: VideoId={VideoId}, Status={Status}", id, video.Status);
                return Ok(response);
            } catch (InvalidOperationException ex) {
                _logger.LogError(ex, "Erro ao consultar status: VideoId={VideoId}", id);
                return StatusCode(500, new { Message = "Erro ao consultar status", Details = ex.Message });
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro inesperado ao consultar status: VideoId={VideoId}", id);
                return StatusCode(500, new { Message = "Erro interno", Details = ex.Message });
            }
        }

        /// <summary>
        /// Consulta resultados da análise de QR Codes
        /// </summary>
        /// <param name="id">ID do vídeo</param>
        /// <returns>Lista de QR Codes com timestamps</returns>
        /// <response code="200">Resultados encontrados</response>
        /// <response code="400">Processamento não concluído ou erro</response>
        /// <response code="404">Vídeo não encontrado</response>
        /// <response code="500">Erro interno</response>
        [HttpGet("{id}/results")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        [ProducesResponseType(typeof(object), 404)]
        [ProducesResponseType(typeof(object), 500)]
        public async Task<IActionResult> GetVideoResults(int id) {
            try {
                if (id <= 0) {
                    return BadRequest(new { Message = "ID do vídeo deve ser maior que zero" });
                }

                _logger.LogDebug("Consultando resultados: VideoId={VideoId}", id);
                var video = await _videoService.BuscaVideoPorIDService(id);

                if (video == null) {
                    _logger.LogWarning("Vídeo não encontrado: VideoId={VideoId}", id);
                    return NotFound(new { Message = "Vídeo não encontrado" });
                }

                if (video.Status == "Erro") {
                    return BadRequest(new {
                        Message = "Processamento falhou",
                        Error = video.ErrorMessage,
                        VideoId = id
                    });
                }

                if (video.Status != "Concluído") {
                    return BadRequest(new {
                        Message = "Processamento não concluído",
                        CurrentStatus = video.Status,
                        VideoId = id,
                        ExpectedStatus = "Concluído"
                    });
                }

                var qrResults = video.QRCodes.Select(qr => new {
                    Content = qr.Content ?? string.Empty,
                    Timestamp = qr.Timestamp,
                    TimestampFormatted = FormatTimestamp(qr.Timestamp)
                }).ToList();

                var processingTime = video.CreatedAt.HasValue
                    ? DateTime.UtcNow - video.CreatedAt.Value
                    : TimeSpan.Zero;

                var response = new {
                    VideoId = video.VideoId,
                    Title = video.Title,
                    Status = video.Status,
                    Duration = video.Duration,
                    TotalQRCodes = qrResults.Count(),
                    QRCodes = qrResults,
                    ProcessingTime = processingTime.ToString(@"hh\:mm\:ss"),
                    AnalysisSummary = new {
                        Density = qrResults.Any() ?
                            Math.Round((double)qrResults.Count() * 60 / video.Duration, 1) : 0,
                        Coverage = qrResults.Any() ?
                            $"{Math.Round((double)(qrResults.Max(r => r.Timestamp) - qrResults.Min(r => r.Timestamp)) / video.Duration * 100, 1)}% do vídeo" : "Nenhum QR Code"
                    }
                };

                _logger.LogInformation("Resultados retornados: VideoId={VideoId}, QRCodes={Count}",
                    id, qrResults.Count());

                return Ok(response);
            } catch (InvalidOperationException ex) {
                _logger.LogError(ex, "Erro ao consultar resultados: VideoId={VideoId}", id);
                return StatusCode(500, new { Message = "Erro ao consultar resultados", Details = ex.Message });
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro inesperado ao consultar resultados: VideoId={VideoId}", id);
                return StatusCode(500, new { Message = "Erro interno", Details = ex.Message });
            }
        }

        #region Métodos Privados

        private static string FormatTimestamp(int seconds) {
            if (seconds < 60)
                return $"{seconds}s";

            var minutes = seconds / 60;
            var remainingSeconds = seconds % 60;

            return remainingSeconds > 0
                ? $"{minutes}m{remainingSeconds}s"
                : $"{minutes}m";
        }

        #endregion
    }
}