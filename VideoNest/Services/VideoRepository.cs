using MongoDB.Driver;
using VideoNest.Models;

namespace VideoNest.Repositories {
    /// <summary>
    /// Repositório MongoDB para vídeos e contadores.
    /// Implementa Counter Pattern para IDs sequenciais atômicos.
    /// </summary>
    /// <remarks>
    /// Coleções: "VideoResults" (vídeos + QRs), "counters" (ID sequencial).
    /// Performance: Índices otimizados para VideoId (O(log n) queries).
    /// </remarks>
    public class VideoRepository : IVideoRepository {
        #region Campos Privados

        private readonly IMongoCollection<VideoResult> _videos;
        private readonly IMongoCollection<VideoCounter> _counters;
        private readonly ILogger<VideoRepository> _logger;

        #endregion

        #region Construtor

        /// <summary>
        /// Inicializa repositório com database e logger.
        /// Configura collections VideoResults e counters.
        /// </summary>
        /// <param name="mongoDatabase">Database MongoDB.</param>
        /// <param name="logger">Logger estruturado (Serilog).</param>
        public VideoRepository(IMongoDatabase mongoDatabase, ILogger<VideoRepository> logger) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _videos = mongoDatabase.GetCollection<VideoResult>("VideoResults");
            _counters = mongoDatabase.GetCollection<VideoCounter>("counters");

            _ = Task.Run(InitializeIndexesAsync);
        }

        #endregion

        #region IVideoRepository - Implementação Corrigida (Sem UpdatedAt)

        /// <summary>
        /// Gera próximo ID sequencial.
        /// Counter Pattern com FindOneAndUpdate atômico.
        /// Operação thread-safe com IsUpsert: true.
        /// </summary>
        /// <returns>ID incremental único para enfileiramento RabbitMQ.</returns>
        /// <exception cref="InvalidOperationException">Falha no contador MongoDB.</exception>
        public async Task<int> GetNextIdAsync() {
            try {
                var filter = Builders<VideoCounter>.Filter.Eq(c => c.Id, "video_counter");
                var update = Builders<VideoCounter>.Update.Inc(c => c.Sequence, 1);
                var options = new FindOneAndUpdateOptions<VideoCounter> {
                    ReturnDocument = ReturnDocument.After,
                    IsUpsert = true
                };

                var counter = await _counters.FindOneAndUpdateAsync(filter, update, options,
                    CancellationToken.None);

                if (counter == null)
                    throw new InvalidOperationException("Falha ao gerar próximo ID");

                _logger.LogDebug("Próximo ID gerado: {Sequence}", counter.Sequence);
                return counter.Sequence;
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao gerar próximo ID");
                throw new InvalidOperationException("Falha no contador MongoDB", ex);
            }
        }

        /// <summary>
        /// Salva vídeo inicial.
        /// Valida campos obrigatórios usando VideoResult.IsValid().
        /// Adiciona CreatedAt timestamp (UTC).
        /// </summary>
        /// <param name="video">Entidade VideoResult com metadados do upload.</param>
        /// <exception cref="ArgumentException">Validação falhou (VideoId, Title, FilePath).</exception>
        /// <exception cref="InvalidOperationException">Falha MongoDB (duplicate key, timeout).</exception>
        public async Task SaveVideoAsync(VideoResult video) {
            if (video == null)
                throw new ArgumentNullException(nameof(video));

            if (!video.IsValid(out var validationError))
                throw new ArgumentException($"Validação falhou: {validationError}", nameof(video));

            video.CreatedAt ??= DateTime.UtcNow;

            try {
                await _videos.InsertOneAsync(video, cancellationToken: CancellationToken.None);
                _logger.LogInformation("Vídeo salvo: VideoId={Id}, Status={Status}, Title={Title}",
                    video.VideoId, video.Status, video.Title);
            } catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey) {
                _logger.LogWarning("Vídeo já existe: VideoId={Id}", video.VideoId);
                throw new InvalidOperationException($"Vídeo {video.VideoId} já existe", ex);
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao salvar vídeo: VideoId={Id}", video.VideoId);
                throw new InvalidOperationException($"Falha ao salvar vídeo {video.VideoId}", ex);
            }
        }

        /// <summary>
        /// Recupera vídeo por ID.
        /// Query otimizada com índice VideoId.
        /// Timeout 5s via CancellationTokenSource.
        /// </summary>
        /// <param name="id">ID do vídeo (sequencial).</param>
        /// <returns>VideoResult completo (Status + QRCodes) ou null (404).</returns>
        /// <exception cref="InvalidOperationException">Falha MongoDB (timeout, conexão).</exception>
        public async Task<VideoResult?> GetVideoByIdAsync(int id) {
            if (id <= 0) {
                _logger.LogWarning("ID inválido para consulta: {VideoId}", id);
                return null;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            try {
                var filter = Builders<VideoResult>.Filter.Eq(v => v.VideoId, id);
                var video = await _videos.Find(filter)
                    .FirstOrDefaultAsync(cts.Token);

                if (video == null) {
                    _logger.LogDebug("Vídeo não encontrado: VideoId={Id}", id);
                } else {
                    _logger.LogDebug("Vídeo carregado: VideoId={Id}, Status={Status}, QRs={Count}",
                        id, video.Status, video.QRCodes?.Count ?? 0);
                }

                return video;
            } catch (OperationCanceledException) when (cts.Token.IsCancellationRequested) {
                _logger.LogWarning("Timeout consulta MongoDB: VideoId={Id}", id);
                return null;
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro consulta MongoDB: VideoId={Id}", id);
                throw new InvalidOperationException($"Falha ao consultar vídeo {id}", ex);
            }
        }

        /// <summary>
        /// Atualiza status do vídeo.
        /// Fluxo: "Na Fila" → "Processando" → "Concluído"/"Erro".
        /// Valida status usando VideoConstants.ValidStatuses.
        /// </summary>
        /// <param name="videoId">ID do vídeo sequencial.</param>
        /// <param name="status">Novo status ("Na Fila", "Processando", "Concluído", "Erro").</param>
        /// <param name="errorMessage">Mensagem de erro (se Status="Erro").</param>
        /// <param name="duration">Duração total em segundos (default 0).</param>
        /// <exception cref="ArgumentException">Status inválido ou ID <= 0.</exception>
        /// <exception cref="InvalidOperationException">Vídeo não encontrado ou falha MongoDB.</exception>
        public async Task UpdateStatusAsync(int videoId, string status, string? errorMessage = null, int duration = 0) {
            if (videoId <= 0)
                throw new ArgumentException("ID inválido", nameof(videoId));
            if (string.IsNullOrWhiteSpace(status))
                throw new ArgumentException("Status obrigatório", nameof(status));

            if (!VideoConstants.ValidStatuses.Contains(status)) {
                _logger.LogWarning("Status inválido: {Status}. Válidos: [{ValidStatuses}]",
                    status, string.Join(", ", VideoConstants.ValidStatuses));
                throw new ArgumentException($"Status inválido: {status}");
            }

            try {
                var filter = Builders<VideoResult>.Filter.Eq(v => v.VideoId, videoId);
                var update = Builders<VideoResult>.Update
                    .Set(v => v.Status, status)
                    .Set(v => v.ErrorMessage, errorMessage)
                    .Set(v => v.Duration, duration);

                var result = await _videos.UpdateOneAsync(filter, update,
                    cancellationToken: CancellationToken.None);

                if (result.IsAcknowledged && result.ModifiedCount > 0) {
                    _logger.LogInformation("Status atualizado: VideoId={Id} → {Status}, Duration={Duration}s",
                        videoId, status, duration);
                } else if (result.MatchedCount == 0) {
                    _logger.LogWarning("Vídeo não encontrado para update: VideoId={Id}", videoId);
                    throw new InvalidOperationException($"Vídeo {videoId} não encontrado");
                } else {
                    _logger.LogDebug("Status já estava atualizado: VideoId={Id}, Status={Status}",
                        videoId, status);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro update status: VideoId={Id}, Status={Status}", videoId, status);
                throw new InvalidOperationException($"Falha ao atualizar status do vídeo {videoId}", ex);
            }
        }

        /// <summary>
        /// Adiciona QR Codes ao vídeo.
        /// Usa PushEach para array atômico no MongoDB.
        /// </summary>
        /// <param name="videoId">ID do vídeo sequencial.</param>
        /// <param name="qrs">Lista de QRCodeResult detectados pelo ScanForge.</param>
        /// <exception cref="ArgumentException">ID inválido ou QRs nulos.</exception>
        /// <exception cref="InvalidOperationException">Vídeo não encontrado ou falha MongoDB.</exception>
        public async Task AddQRCodesAsync(int videoId, List<QRCodeResult> qrs) {
            if (videoId <= 0)
                throw new ArgumentException("ID inválido", nameof(videoId));
            if (qrs == null || !qrs.Any()) {
                _logger.LogDebug("Nenhum QR Code para adicionar: VideoId={Id}", videoId);
                return;
            }

            var validQrs = qrs.Where(qr => qr.IsValid()).ToList();

            if (validQrs.Count == 0) {
                _logger.LogDebug("Nenhum QR Code válido para adicionar: VideoId={Id}", videoId);
                return;
            }

            if (validQrs.Count != qrs.Count) {
                _logger.LogWarning("Removidas {InvalidCount} QRs inválidos: VideoId={Id}",
                    qrs.Count - validQrs.Count, videoId);
            }

            var uniqueQrs = validQrs
                .GroupBy(qr => $"{qr.Content}_{qr.Timestamp}")
                .Select(g => g.First())
                .ToList();

            if (uniqueQrs.Count != validQrs.Count) {
                _logger.LogWarning("Removidas {Duplicates} QRs duplicados: VideoId={Id}",
                    validQrs.Count - uniqueQrs.Count, videoId);
            }

            try {
                var filter = Builders<VideoResult>.Filter.Eq(v => v.VideoId, videoId);
                var update = Builders<VideoResult>.Update
                    .PushEach(v => v.QRCodes, uniqueQrs);

                var result = await _videos.UpdateOneAsync(filter, update,
                    cancellationToken: CancellationToken.None);

                if (result.IsAcknowledged && result.ModifiedCount > 0) {
                    _logger.LogInformation("QRs adicionados: VideoId={Id}, Count={Count}",
                        videoId, uniqueQrs.Count);
                } else if (result.MatchedCount == 0) {
                    _logger.LogWarning("Vídeo não encontrado para QRs: VideoId={Id}", videoId);
                    throw new InvalidOperationException($"Vídeo {videoId} não encontrado");
                } else {
                    _logger.LogDebug("QRs já estavam presentes: VideoId={Id}, Count={Count}",
                        videoId, uniqueQrs.Count);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro adicionar QRs: VideoId={Id}, Count={Count}",
                    videoId, uniqueQrs.Count);
                throw new InvalidOperationException($"Falha ao adicionar QRs ao vídeo {videoId}", ex);
            }
        }

        #endregion

        #region Métodos Privados - Inicialização (Opcional)

        /// <summary>
        /// Cria índices otimizados para performance (executado em background).
        /// Índice único: VideoId .
        /// Índice composto: Status (relatórios por estado).
        /// </summary>
        private async Task InitializeIndexesAsync() {
            try {
                // ✅ PASSO 1: Índice único em VideoId (não _id)
                var videoIdIndex = Builders<VideoResult>.IndexKeys.Ascending(x => x.VideoId);
                var videoIdIndexModel = new CreateIndexModel<VideoResult>(
                    videoIdIndex, new CreateIndexOptions { Unique = true });

                await _videos.Indexes.CreateOneAsync(videoIdIndexModel);
                _logger.LogInformation("✅ Índice único VideoId criado");

                // ✅ PASSO 2: Índice em Status para queries de relatórios
                var statusIndex = Builders<VideoResult>.IndexKeys.Ascending(x => x.Status);
                await _videos.Indexes.CreateOneAsync(new CreateIndexModel<VideoResult>(statusIndex));
                _logger.LogInformation("✅ Índice Status criado");

                // ✅ PASSO 3: Índice composto para queries de resultados (VideoId + CreatedAt)
                var compoundIndex = Builders<VideoResult>.IndexKeys
                    .Ascending(x => x.VideoId)
                    .Descending(x => x.CreatedAt);
                await _videos.Indexes.CreateOneAsync(new CreateIndexModel<VideoResult>(compoundIndex));
                _logger.LogInformation("✅ Índice composto VideoId+CreatedAt criado");
            } catch (MongoCommandException ex) when (ex.Code == 85) // IndexOptionsConflict
              {
                _logger.LogDebug("ℹ️ Índices MongoDB já existem - ignorando");
            } catch (MongoCommandException ex) when (ex.Code == 11000) // DuplicateKey
              {
                _logger.LogWarning(ex, "⚠️ Conflito de índice duplicado - continuando");
            } catch (Exception ex) {
                _logger.LogWarning(ex, "⚠️ Falha ao criar índices MongoDB (continuando sem índices otimizados)");
            }
        }

        #endregion
    }
}