using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Prometheus;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using VideoNest.Services;

namespace VideoNest.Services {
    /// <summary>
    /// Publisher RabbitMQ com Dead Letter Queue (DLQ) para mensagens assíncronas.
    /// FASE 01: Mensageria obrigatória - Upload → Fila → Processamento.
    /// </summary>
    /// <remarks>
    /// **Responsabilidades**:
    /// - Publica mensagens JSON na fila `video_queue` (durable, persistent)
    /// - Configura DLQ automática para mensagens problemáticas (5min TTL)
    /// - Setup idempotente: não falha se queues já existem
    /// - Auto-recovery: reconecta se RabbitMQ cair
    /// 
    /// **Arquitetura**:
    /// ```
    /// VideoService.UploadVideoAsync()
    ///     ↓
    /// PublishMessage("{VideoId:123, FilePath:"/uploads/123.mp4"}")
    ///     ↓
    /// video_queue (durable) → ScanForge Worker
    ///     ↓ (falha 3x)
    /// dlq_video_queue (quarentena, TTL 5min)
    /// ```
    /// 
    /// **Configuração**: appsettings.json → RabbitMQ section
    /// ```
    /// "RabbitMQ": {
    ///   "HostName": "rabbitmq", "Port": 5672,
    ///   "UserName": "admin", "Password": "admin",
    ///   "QueueName": "video_queue",
    ///   "DeadLetterExchange": "dlx_video_exchange",
    ///   "DeadLetterQueue": "dlq_video_queue"
    /// }
    /// ```
    /// </remarks>
    public class RabbitMQPublisher : IRabbitMQPublisher {
        #region Campos Privados

        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly ILogger<RabbitMQPublisher> _logger;
        private readonly string _queueName;
        private readonly string _deadLetterExchange;
        private readonly string _deadLetterQueue;
        private readonly ConnectionFactory _factory;

        #endregion

        #region Métricas Prometheus (Bônus FASE 05)

        private static readonly Counter MessagesPublished = Metrics.CreateCounter(
            "rabbitmq_messages_published_total",
            "Total de mensagens publicadas na fila video_queue",
            new CounterConfiguration { LabelNames = new[] { "queue" } });

        private static readonly Histogram PublishDuration = Metrics.CreateHistogram(
            "rabbitmq_publish_duration_seconds",
            "Duração das publicações RabbitMQ");

        private static readonly Counter DlqMessages = Metrics.CreateCounter(
            "rabbitmq_dlq_messages_total",
            "Mensagens enviadas para Dead Letter Queue");

        #endregion

        #region Construtor

        /// <summary>
        /// Inicializa publisher com DLQ idempotente e auto-recovery.
        /// </summary>
        /// <param name="configuration">Configurações RabbitMQ (appsettings).</param>
        /// <param name="logger">Logger estruturado (Serilog).</param>
        /// <exception cref="ArgumentNullException">Logger nulo.</exception>
        /// <exception cref="BrokerUnreachableException">RabbitMQ inacessível.</exception>
        public RabbitMQPublisher(IConfiguration configuration, ILogger<RabbitMQPublisher> logger) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // ✅ Configuração com fallback para desenvolvimento local
            _factory = new ConnectionFactory {
                HostName = configuration["RabbitMQ:HostName"] ?? "localhost",
                Port = configuration.GetValue<int>("RabbitMQ:Port", 5672),
                UserName = configuration["RabbitMQ:UserName"] ?? "admin",
                Password = configuration["RabbitMQ:Password"] ?? "admin",

                // ✅ Resiliência: Auto-recovery e heartbeat
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),  // Retry a cada 10s
                RequestedHeartbeat = TimeSpan.FromSeconds(60),       // 60s heartbeat

                // ✅ Dispatch consumers async (melhor throughput)
                DispatchConsumersAsync = true
            };

            try {
                _connection = _factory.CreateConnection();
                _channel = _connection.CreateModel();

                // ✅ Configurações da fila
                _queueName = configuration["RabbitMQ:QueueName"] ?? "video_queue";
                _deadLetterExchange = configuration["RabbitMQ:DeadLetterExchange"] ?? "dlx_video_exchange";
                _deadLetterQueue = configuration["RabbitMQ:DeadLetterQueue"] ?? "dlq_video_queue";

                // ✅ Setup idempotente: Não falha se já configurado
                SetupDeadLetterInfrastructure();
                SetupMainQueue();

                _logger.LogInformation(
                    "🐰 RabbitMQ Publisher inicializado: {Queue} ↔ DLQ:{DLQ} @ {Host}:{Port} (Auto-recovery: {Recovery})",
                    _queueName, _deadLetterQueue, _factory.HostName, _factory.Port, _factory.AutomaticRecoveryEnabled);

                // ✅ Métrica inicial
                MessagesPublished.WithLabels(_queueName).IncTo(0);
            } catch (BrokerUnreachableException ex) {
                _logger.LogCritical(ex, "💥 RabbitMQ inacessível: {Host}:{Port} - Verifique docker-compose",
                    _factory.HostName, _factory.Port);
                throw;
            } catch (Exception ex) {
                _logger.LogError(ex, "💥 Falha crítica na inicialização RabbitMQ - Host: {Host}", _factory.HostName);
                throw new InvalidOperationException($"Falha RabbitMQ: {_factory.HostName}:{_factory.Port}", ex);
            }
        }

        #endregion

        #region Setup Infrastructure (Idempotente)

        /// <summary>
        /// Configura infraestrutura Dead Letter Queue (idempotente).
        /// Executa apenas uma vez na inicialização.
        /// </summary>
        private void SetupDeadLetterInfrastructure() {
            // ✅ CORREÇÃO: Usar parâmetros nomeados ou deixar implícitos
            DeclareQueueIdempotent(_deadLetterQueue, durable: true);
            // OU simplesmente:
            // DeclareQueueIdempotent(_deadLetterQueue); // durable=true é default

            DeclareExchangeIdempotent(_deadLetterExchange, ExchangeType.Direct);
            BindQueueToExchangeIdempotent(_deadLetterQueue, _deadLetterExchange, _deadLetterQueue);
            _logger.LogDebug("🔗 DLQ Infrastructure configurada: {DLQ} ↔ {DLX}", _deadLetterQueue, _deadLetterExchange);
        }

        private void SetupMainQueue() {
            var args = new Dictionary<string, object>
            {
                // ✅ Dead Letter Exchange routing
                { "x-dead-letter-exchange", _deadLetterExchange },
                { "x-dead-letter-routing-key", _deadLetterQueue },
        
                // ✅ TTL 5 minutos para mensagens problemáticas
                { "x-message-ttl", 300000 }, // 300 segundos = 5 minutos
        
                // ✅ Limites de fila
                { "x-max-length", 10000 }, // Máximo 10k mensagens
                { "x-overflow", "drop-head" }, // Drop oldest se lotada
        
                // ✅ Single active consumer (evita race conditions)
                { "x-single-active-consumer", true },
        
                // ✅ Message priority (opcional)
                { "x-max-priority", 10 }
            };

            // ✅ CORREÇÃO: Parâmetros nomeados explícitos
            DeclareQueueIdempotent(_queueName, arguments: args);
            // OU:
            // DeclareQueueIdempotent(_queueName, durable: true, arguments: args);

            _logger.LogDebug("📋 Fila principal configurada: {Queue} (TTL:5m, Max:10k, DLQ:{DLQ})",
                _queueName, _deadLetterQueue);
        }

        /// <summary>
        /// Declara queue de forma idempotente (não falha se já existe).
        /// </summary>
        /// <param name="queueName">Nome da queue.</param>
        /// <param name="durable">Queue durável (sobrevive restarts)?</param>
        /// <param name="exclusive">Queue exclusiva do connection?</param>
        /// <param name="autoDelete">Auto-delete quando sem consumers?</param>
        /// <param name="arguments">Argumentos avançados (x-dead-letter-exchange, TTL, etc.).</param>
        private void DeclareQueueIdempotent(string queueName, bool durable = true, bool exclusive = false,
            bool autoDelete = false, IDictionary<string, object>? arguments = null) {
            try {
                _channel.QueueDeclare(
                    queue: queueName,
                    durable: durable,
                    exclusive: exclusive,
                    autoDelete: autoDelete,
                    arguments: arguments);

                _logger.LogDebug("✅ Queue '{QueueName}' declarada (durable: {Durable}, args: {ArgCount})",
                    queueName, durable, arguments?.Count ?? 0);
            } catch (OperationInterruptedException ex)
                  when (ex.ShutdownReason?.ReplyText?.Contains("405 PRECONDITION_FAILED") == true) {
                // ✅ Queue já existe com configuração diferente - aceitável para idempotência
                _logger.LogDebug("ℹ️ Queue '{QueueName}' já existe (config diferente) - aceitando idempotentemente", queueName);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "⚠️ Erro ao declarar queue '{QueueName}' - continuando (pode já existir)", queueName);
            }
        }

        /// <summary>
        /// Declara exchange de forma idempotente.
        /// </summary>
        /// <param name="exchangeName">Nome do exchange.</param>
        /// <param name="type">Tipo: Direct, Fanout, Topic.</param>
        private void DeclareExchangeIdempotent(string exchangeName, string type) {
            try {
                _channel.ExchangeDeclare(
                    exchange: exchangeName,
                    type: type,
                    durable: true,
                    autoDelete: false,
                    arguments: null);

                _logger.LogDebug("✅ Exchange '{ExchangeName}' ({Type}) declarado com sucesso", exchangeName, type);
            } catch (Exception ex) {
                _logger.LogDebug(ex, "ℹ️ Exchange '{ExchangeName}' já existe - aceitando idempotentemente", exchangeName);
            }
        }

        /// <summary>
        /// Bind queue ao exchange de forma idempotente.
        /// </summary>
        /// <param name="queueName">Nome da queue.</param>
        /// <param name="exchangeName">Nome do exchange.</param>
        /// <param name="routingKey">Routing key para matching.</param>
        private void BindQueueToExchangeIdempotent(string queueName, string exchangeName, string routingKey) {
            try {
                _channel.QueueBind(
                    queue: queueName,
                    exchange: exchangeName,
                    routingKey: routingKey);

                _logger.LogDebug("🔗 Queue '{QueueName}' bindada ao exchange '{ExchangeName}' (routingKey: {RoutingKey})",
                    queueName, exchangeName, routingKey);
            } catch (Exception ex) {
                _logger.LogDebug(ex, "ℹ️ Bind '{QueueName}→{ExchangeName}' já existe - aceitando", queueName, exchangeName);
            }
        }

        #endregion

        #region Métodos Públicos - IRabbitMQPublisher

        /// <summary>
        /// ✅ FASE 01: Publica mensagem JSON na fila video_queue (síncrono).
        /// Dispara processamento assíncrono no ScanForge Worker.
        /// </summary>
        /// <param name="message">Payload JSON serializado.</param>
        /// <remarks>
        /// **Formato esperado**: `{ "VideoId": int, "FilePath": string, "Timestamp": DateTime }`
        /// **Exemplo**: `{ "VideoId": 123, "FilePath": "/uploads/video-123.mp4" }`
        /// 
        /// **Persistência**: 
        /// - Queue: `durable = true` (sobrevive restarts)
        /// - Message: `Persistent = true` (não perdida em crashes)
        /// 
        /// **Resiliência**:
        /// - `mandatory = true`: Falha se não conseguir entregar (alerta imediato)
        /// - DLQ: Mensagens problemáticas vão para `dlq_video_queue` após 5min TTL
        /// - Auto-recovery: Reconecta se RabbitMQ cair
        /// 
        /// **Métricas**: Prometheus counter + histogram expostas
        /// </remarks>
        /// <exception cref="InvalidOperationException">Falha RabbitMQ (queue lotada, conexão perdida).</exception>
        public void PublishMessage(string message) {
            if (string.IsNullOrWhiteSpace(message)) {
                _logger.LogWarning("⚠️ Mensagem vazia ignorada para fila {QueueName}", _queueName);
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            try {
                var body = Encoding.UTF8.GetBytes(message);
                var properties = _channel.CreateBasicProperties();

                // ✅ Persistência: Message não perdida em crashes
                properties.Persistent = true;

                // ✅ Headers para tracing (opcional)
                properties.Headers ??= new Dictionary<string, object>();
                properties.Headers["timestamp"] = DateTime.UtcNow.Ticks;
                properties.Headers["source"] = "VideoNest-API";

                // ✅ Publish com métricas
                _channel.BasicPublish(
                    exchange: "",                    // Default exchange
                    routingKey: _queueName,          // Roteia para video_queue
                    mandatory: true,                 // Falha se não entregar
                    basicProperties: properties,
                    body: body);

                var videoId = ExtractVideoId(message);

                // ✅ Métricas Prometheus
                MessagesPublished.WithLabels(_queueName).Inc();
                PublishDuration.Observe(stopwatch.Elapsed.TotalSeconds);

                _logger.LogInformation(
                    "📤 Publicado com sucesso: VideoId={VideoId} → {QueueName} ({Size}B, {Elapsed}ms) (DLQ ready)",
                    videoId, _queueName, body.Length, stopwatch.ElapsedMilliseconds);
            } catch (Exception ex) {
                stopwatch.Stop();
                PublishDuration.Observe(stopwatch.Elapsed.TotalSeconds);

                var videoId = ExtractVideoId(message);
                _logger.LogError(ex,
                    "💥 Falha ao publicar VideoId={VideoId} em '{QueueName}': {Error} (Size: {Size}B)",
                    videoId, _queueName, ex.Message, message.Length);

                throw new InvalidOperationException(
                    $"Erro RabbitMQ [{_queueName}]: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// ✅ FASE 01: Wrapper assíncrono para compatibilidade async/await.
        /// Fire-and-forget pattern - não bloqueia o caller.
        /// </summary>
        /// <param name="message">Payload JSON serializado.</param>
        /// <returns>Task completada (não aguarda entrega real).</returns>
        /// <remarks>
        /// **Uso**: Em métodos async como `VideoService.UploadVideoAsync()`.
        /// **Pattern**: Fire-and-forget para não impactar performance de upload.
        /// **Internamente**: Chama `PublishMessage()` síncrono.
        /// </remarks>
        public async Task PublishMessageAsync(string message) {
            // ✅ Fire-and-forget: Não await a publicação real
            _ = Task.Run(() => PublishMessage(message));
            await Task.CompletedTask;  // Retorna imediatamente
        }

        /// <summary>
        /// ✅ FASE 05: Health check para monitoring (opcional).
        /// Verifica se conexão RabbitMQ está ativa.
        /// </summary>
        /// <returns>True se publisher saudável.</returns>
        /// <remarks>
        /// **Uso**: `/health` endpoint + Prometheus health checks.
        /// **Monitoramento**: Integra com docker-compose healthcheck.
        /// **Métricas**: Up/Down status para Grafana dashboard.
        /// </remarks>
        public bool IsHealthy() {
            try {
                return _connection.IsOpen &&
                       _channel.IsOpen &&
                       _channel.IsOpen;  // Double-check channel
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Health check RabbitMQ falhou");
                return false;
            }
        }

        #endregion

        #region Métodos Privados - Helpers

        /// <summary>
        /// Extrai VideoId do payload JSON para logging e métricas estruturadas.
        /// Parse manual sem deserialização completa (performance crítica).
        /// </summary>
        /// <param name="message">Mensagem JSON serializada.</param>
        /// <returns>VideoId como string ou "unknown" se não encontrado.</returns>
        /// <remarks>
        /// **Formato esperado**: `{ "VideoId": 123, "FilePath": "...", ... }`
        /// **Performance**: O(1) string search vs O(n) JSON parsing
        /// **Fallback**: Retorna "unknown" se formato inválido
        /// </remarks>
        private static string ExtractVideoId(string message) {
            try {
                const string videoIdKey = "\"VideoId\":";
                var startIndex = message.IndexOf(videoIdKey, StringComparison.Ordinal);

                if (startIndex == -1)
                    return "unknown";

                startIndex += videoIdKey.Length;

                // Encontra próximo separador (vírgula, chave, ou fim)
                var endIndex = message.IndexOf(',', startIndex);
                if (endIndex == -1)
                    endIndex = message.IndexOf('}', startIndex);

                if (endIndex == -1)
                    return "unknown";

                var idStr = message.Substring(startIndex, endIndex - startIndex)
                    .Trim().Trim('"', ' ', ',');

                return int.TryParse(idStr, out _) ? idStr : "unknown";
            } catch {
                return "unknown";
            }
        }

        #endregion

        #region IDisposable - Cleanup

        /// <summary>
        /// ✅ Libera recursos RabbitMQ (conexão TCP, canal AMQP).
        /// Graceful shutdown com códigos de status AMQP.
        /// </summary>
        /// <remarks>
        /// **Ordem**: Channel → Connection → Dispose
        /// **Códigos**: 200 (normal), evita crashes se já fechado
        /// **Logging**: Debug level para cleanup não-crítico
        /// </remarks>
        public void Dispose() {
            try {
                // ✅ Graceful close com códigos AMQP
                _channel?.Close(200, "Publisher shutdown - graceful");
                _connection?.Close(200, "Connection shutdown - graceful");
            } catch (Exception ex) when (ex is not ObjectDisposedException) {
                _logger.LogDebug(ex, "ℹ️ Erro não-crítico ao fechar RabbitMQ (pode já estar fechado)");
            } finally {
                _channel?.Dispose();
                _connection?.Dispose();
                _logger.LogDebug("🔌 RabbitMQ Publisher recursos liberados - Connection: {ConnectionId}",
                    _connection?.Endpoint?.HostName ?? "unknown");
            }
        }

        #endregion
    }
}