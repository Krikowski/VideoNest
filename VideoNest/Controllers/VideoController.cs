using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VideoNest.DTO;
using VideoNest.Hubs;
using VideoNest.Models;
using VideoNest.Services;

namespace VideoNest.Controllers {
    /// <summary>
    /// Controller para gerenciamento de vídeos
    /// FASE 02-04: Upload, status e resultados
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
        /// FASE 02: Upload de vídeo via API
        /// </summary>
        /// <remarks>
        /// Envia um arquivo de vídeo (.mp4, .avi) para processamento assíncrono.
        /// O vídeo é salvo, registrado no MongoDB e adicionado à fila RabbitMQ.
        /// </remarks>
        /// <param name="request">Dados do upload incluindo arquivo e metadados</param>
        /// <returns>ID do vídeo e mensagem de confirmação</returns>
        /// <response code="200">Upload realizado com sucesso</response>
        /// <response code="400">Arquivo inválido ou parâmetros incorretos</response>
        /// <response code="500">Erro interno do servidor</response>
        [HttpPost]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        [ProducesResponseType(typeof(object), 500)]
        public async Task<IActionResult> UploadVideo([FromForm] VideoUploadRequest request) {
            try {
                // Validação inicial no controller (defesa em profundidade)
                if (request?.File == null || request.File.Length == 0) {
                    _logger.LogWarning("Tentativa de upload sem arquivo");
                    return BadRequest(new { Message = "Arquivo de vídeo é obrigatório." });
                }

                var allowedExtensions = new[] { ".mp4", ".avi" };
                var extension = Path.GetExtension(request.File.FileName)?.ToLowerInvariant();

                if (!allowedExtensions.Contains(extension)) {
                    _logger.LogWarning("Formato inválido enviado: {Extension}", extension);
                    return BadRequest(new {
                        Message = "Formato inválido. Aceitos: .mp4, .avi",
                        AllowedFormats = allowedExtensions
                    });
                }

                // Validação de tamanho (100MB)
                if (request.File.Length > 100 * 1024 * 1024) {
                    _logger.LogWarning("Arquivo muito grande: {Size} bytes", request.File.Length);
                    return BadRequest(new {
                        Message = "Arquivo muito grande. Máximo: 100MB",
                        MaxSize = "100MB"
                    });
                }

                _logger.LogInformation("Iniciando upload: {Title} ({Size} bytes)",
                    request.Title ?? "Sem título", request.File.Length);

                // Chamar serviço (FASE 02)
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                int videoId = await _videoService.UploadVideoAsync(request.File, request);
                stopwatch.Stop();

                _logger.LogInformation("Upload concluído: VideoId={VideoId} em {ElapsedMs}ms",
                    videoId, stopwatch.ElapsedMilliseconds);

                return Ok(new {
                    VideoId = videoId,
                    Message = "Vídeo enviado para processamento assíncrono.",
                    ElapsedMs = stopwatch.ElapsedMilliseconds
                });
            } catch (ArgumentException ex) {
                _logger.LogWarning(ex, "Erro de validação no upload: {Title}", request?.Title);
                return BadRequest(new { Message = ex.Message });
            } catch (InvalidOperationException ex) {
                _logger.LogError(ex, "Erro operacional no upload: {Title}", request?.Title);
                return StatusCode(500, new { Message = "Erro ao processar upload", Details = ex.Message });
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro inesperado no upload: {Title}", request?.Title);
                return StatusCode(500, new { Message = "Erro interno do servidor", Details = ex.Message });
            }
        }

        /// <summary>
        /// FASE 01: Teste de fila RabbitMQ (endpoint temporário)
        /// </summary>
        /// <remarks>
        /// Envia uma mensagem de teste para a fila RabbitMQ.
        /// Útil para validar a configuração de mensageria durante desenvolvimento.
        /// </remarks>
        /// <param name="request">Mensagem de teste</param>
        /// <returns>Confirmação de envio</returns>
        /// <response code="200">Mensagem enviada com sucesso</response>
        /// <response code="400">Mensagem inválida</response>
        [HttpPost("testqueue")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        public IActionResult TestQueue([FromBody] TestQueueRequest request) {
            try {
                if (string.IsNullOrWhiteSpace(request?.Message)) {
                    return BadRequest(new { Message = "Mensagem de teste é obrigatória" });
                }

                _videoService.PublishTestMessage(request.Message);

                _logger.LogInformation("Teste de fila executado: {Message}", request.Message);

                return Ok(new {
                    Message = "Mensagem de teste enviada para fila com sucesso",
                    Queue = "video_queue",
                    Content = request.Message
                });
            } catch (InvalidOperationException ex) {
                _logger.LogError(ex, "Erro no teste de fila: {Message}", request?.Message);
                return StatusCode(500, new { Message = "Erro ao testar fila", Details = ex.Message });
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro inesperado no teste de fila");
                return StatusCode(500, new { Message = "Erro interno", Details = ex.Message });
            }
        }

        /// <summary>
        /// FASE 02: Consulta do status do processamento
        /// </summary>
        /// <remarks>
        /// Retorna o status atual do processamento do vídeo.
        /// Status possíveis: "Na Fila", "Processando", "Concluído", "Erro".
        /// </remarks>
        /// <param name="id">ID do vídeo</param>
        /// <returns>Status atual e metadados</returns>
        /// <response code="200">Status encontrado</response>
        /// <response code="404">Vídeo não encontrado</response>
        /// <response code="500">Erro interno</response>
        [HttpGet("{id}/status")]
        [ProducesResponseType(typeof(object), 200)]
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
                    Progress = GetProgressPercentage(video.Status)
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
        /// FASE 02: Consulta dos resultados do processamento
        /// </summary>
        /// <remarks>
        /// Retorna a lista de QR Codes detectados com timestamps.
        /// Disponível apenas após conclusão do processamento.
        /// </remarks>
        /// <param name="id">ID do vídeo</param>
        /// <returns>Resultados do processamento</returns>
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

                // Validação de status
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
                        VideoId = id
                    });
                }

                var qrResults = video.QRCodes.Select(qr => new {
                    Content = qr.Content ?? string.Empty,
                    Timestamp = qr.Timestamp,
                    TimestampFormatted = $"{qr.Timestamp}s"
                }).ToList();

                var response = new {
                    VideoId = video.VideoId,
                    Title = video.Title,
                    Status = video.Status,
                    Duration = video.Duration,
                    TotalQRCodes = qrResults.Count,
                    QRCodes = qrResults,
                    ProcessingTime = video.CreatedAt.HasValue ?
                        DateTime.UtcNow - video.CreatedAt.Value : TimeSpan.Zero
                };

                _logger.LogInformation("Resultados retornados: VideoId={VideoId}, QRCodes={Count}",
                    id, qrResults.Count);

                return Ok(response);
            } catch (InvalidOperationException ex) {
                _logger.LogError(ex, "Erro ao consultar resultados: VideoId={VideoId}", id);
                return StatusCode(500, new { Message = "Erro ao consultar resultados", Details = ex.Message });
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro inesperado ao consultar resultados: VideoId={VideoId}", id);
                return StatusCode(500, new { Message = "Erro interno", Details = ex.Message });
            }
        }

        /// <summary>
        /// FASE 03: Atualizar status do vídeo (endpoint interno para ScanForge)
        /// </summary>
        /// <param name="videoId">ID do vídeo</param>
        /// <param name="request">Dados de status</param>
        /// <returns>Confirmação de atualização</returns>
        [HttpPut("{id}/status")]
        [ApiExplorerSettings(IgnoreApi = true)] // Endpoint interno
        public async Task<IActionResult> UpdateVideoStatus(int id, [FromBody] UpdateStatusRequest request) {
            try {
                if (id <= 0 || string.IsNullOrWhiteSpace(request.Status)) {
                    return BadRequest(new { Message = "Parâmetros inválidos" });
                }

                await _videoService.UpdateVideoStatusAsync(id, request.Status, request.ErrorMessage, request.Duration);

                return Ok(new {
                    Message = "Status atualizado com sucesso",
                    VideoId = id,
                    NewStatus = request.Status
                });
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao atualizar status: VideoId={VideoId}", id);
                return StatusCode(500, new { Message = "Erro ao atualizar status", Details = ex.Message });
            }
        }

        /// <summary>
        /// FASE 03: Adicionar QR Codes (endpoint interno para ScanForge)
        /// </summary>
        /// <param name="videoId">ID do vídeo</param>
        /// <param name="request">Lista de QR Codes</param>
        /// <returns>Confirmação de adição</returns>
        [HttpPost("{id}/qrcodes")]
        [ApiExplorerSettings(IgnoreApi = true)] // Endpoint interno
        public async Task<IActionResult> AddQRCodes(int id, [FromBody] AddQRCodesRequest request) {
            try {
                if (id <= 0 || request.QRCodes == null || !request.QRCodes.Any()) {
                    return BadRequest(new { Message = "Parâmetros inválidos" });
                }

                await _videoService.AddQRCodesToVideoAsync(id, request.QRCodes);

                // Notificar conclusão (FASE 04)
                await _videoService.NotifyVideoCompletedAsync(id, request.QRCodes);

                return Ok(new {
                    Message = "QR Codes adicionados com sucesso",
                    VideoId = id,
                    TotalQRCodes = request.QRCodes.Count
                });
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao adicionar QR Codes: VideoId={VideoId}", id);
                return StatusCode(500, new { Message = "Erro ao adicionar QR Codes", Details = ex.Message });
            }
        }

        #region Métodos Privados

        private static int GetProgressPercentage(string status) {
            return status switch {
                "Na Fila" => 10,
                "Processando" => 50,
                "Concluído" => 100,
                "Erro" => 0,
                _ => 0
            };
        }

        #endregion
    }

    #region DTOs para Requests

    /// <summary>
    /// Request para teste de fila (FASE 01)
    /// </summary>
    public class TestQueueRequest {
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request para atualização de status (FASE 03)
    /// </summary>
    public class UpdateStatusRequest {
        public string Status { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public int Duration { get; set; }
    }

    /// <summary>
    /// Request para adição de QR Codes (FASE 03)
    /// </summary>
    public class AddQRCodesRequest {
        public List<QRCodeResult> QRCodes { get; set; } = new();
    }

    #endregion
}