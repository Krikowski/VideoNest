using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Text;
using VideoNest.Services;

namespace VideoNest.Service {
    /// <summary>
    /// Publisher RabbitMQ com Dead Letter Queue (BONUS)
    /// FASE 01: Mensageria assíncrona com retry e DLQ
    /// </summary>
    public class RabbitMQPublisher : IRabbitMQPublisher, IDisposable {
        private readonly string _hostName;
        private readonly string _userName;
        private readonly string _password;
        private readonly int _port;
        private readonly string _queueName;
        private readonly string _deadLetterExchange;
        private readonly string _deadLetterQueue;
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly ILogger<RabbitMQPublisher> _logger;
        private bool _disposed = false;

        public RabbitMQPublisher(IConfiguration configuration, ILogger<RabbitMQPublisher> logger) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _hostName = configuration["RabbitMQ:HostName"] ?? "rabbitmq";
            _userName = configuration["RabbitMQ:UserName"] ?? "admin";
            _password = configuration["RabbitMQ:Password"] ?? "admin";
            _port = int.Parse(configuration["RabbitMQ:Port"] ?? "5672");
            _queueName = configuration["RabbitMQ:QueueName"] ?? "video_queue";
            _deadLetterExchange = configuration["RabbitMQ:DeadLetterExchange"] ?? "dlx_video_queue";
            _deadLetterQueue = configuration["RabbitMQ:DeadLetterQueue"] ?? "dlq_video_queue";

            var factory = new ConnectionFactory {
                HostName = _hostName,
                UserName = _userName,
                Password = _password,
                Port = _port,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(30),
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            try {
                _connection = factory.CreateConnection();
                _channel = _connection.CreateModel();
                _channel.ConfirmSelect(); // Publisher confirms (BONUS)

                SetupQueues();
                _logger.LogInformation("RabbitMQ Publisher inicializado: Host={HostName}, Queue={QueueName}", _hostName, _queueName);
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao inicializar RabbitMQ Publisher");
                throw new InvalidOperationException("Falha ao conectar com RabbitMQ", ex);
            }
        }

        /// <summary>
        /// Configura queues com Dead Letter Queue (BONUS)
        /// </summary>
        private void SetupQueues() {
            try {
                // Dead Letter Exchange
                _channel.ExchangeDeclare(_deadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false);

                // Dead Letter Queue
                var dlqArgs = new Dictionary<string, object>
                {
                    { "x-message-ttl", 86400000 }, // 24h TTL para DLQ
                    { "x-dead-letter-exchange", string.Empty } // Não reencaminha
                };
                _channel.QueueDeclare(_deadLetterQueue, durable: true, exclusive: false, autoDelete: false, arguments: dlqArgs);
                _channel.QueueBind(_deadLetterQueue, _deadLetterExchange, _deadLetterQueue);

                // Queue principal com DLQ
                var mainQueueArgs = new Dictionary<string, object>
                {
                    { "x-dead-letter-exchange", _deadLetterExchange },
                    { "x-dead-letter-routing-key", _deadLetterQueue },
                    { "x-message-ttl", 300000 }, // 5min TTL antes de ir para DLQ
                    { "x-max-length", 10000 } // Limite de mensagens
                };

                _channel.QueueDeclare(_queueName, durable: true, exclusive: false, autoDelete: false, arguments: mainQueueArgs);

                _logger.LogDebug("Queues configuradas: {QueueName} -> DLQ: {DeadLetterQueue}", _queueName, _deadLetterQueue);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Erro ao configurar queues. Queue pode já existir: {QueueName}", _queueName);
            }
        }

        /// <summary>
        /// Publica mensagem na fila RabbitMQ (ÚNICO método)
        /// </summary>
        public void PublishMessage(string message) {
            if (string.IsNullOrWhiteSpace(message)) {
                throw new ArgumentException("Mensagem não pode ser vazia", nameof(message));
            }

            if (_disposed) {
                throw new ObjectDisposedException(nameof(RabbitMQPublisher));
            }

            try {
                var body = Encoding.UTF8.GetBytes(message);
                var properties = _channel.CreateBasicProperties();

                properties.Persistent = true;
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                properties.ContentType = "application/json";
                properties.DeliveryMode = 2; // Persistent

                _channel.BasicPublish(
                    exchange: "",
                    routingKey: _queueName,
                    basicProperties: properties,
                    body: body);

                // Aguardar confirmação (BONUS)
                if (!_channel.WaitForConfirms(TimeSpan.FromSeconds(5))) {
                    throw new TimeoutException("RabbitMQ não confirmou publicação da mensagem");
                }

                _logger.LogInformation("📤 Mensagem publicada na fila '{QueueName}': {MessageLength} bytes", _queueName, body.Length);
            } catch (Exception ex) {
                _logger.LogError(ex, "Erro ao publicar mensagem na fila '{QueueName}'", _queueName);
                throw new InvalidOperationException($"Falha ao publicar mensagem na fila {_queueName}", ex);
            }
        }

        /// <summary>
        /// Obtém estatísticas da queue (método de diagnóstico)
        /// </summary>
        public QueueDeclareOk GetQueueInfo() {
            if (_disposed) throw new ObjectDisposedException(nameof(RabbitMQPublisher));

            return _channel.QueueDeclarePassive(_queueName);
        }

        /// <summary>
        /// Limpeza de recursos
        /// </summary>
        public void Dispose() {
            if (_disposed) return;

            try {
                _channel?.Close(200, "Publisher fechando");
                _channel?.Dispose();
                _connection?.Close(200, "Connection fechando");
                _connection?.Dispose();
                _logger.LogDebug("RabbitMQ Publisher disposto");
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Erro ao fechar conexão RabbitMQ");
            } finally {
                _disposed = true;
            }
        }
    }
}