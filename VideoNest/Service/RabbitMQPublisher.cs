using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VideoNest.Services;

/// <summary>
/// Publicador responsável por declarar a infraestrutura RabbitMQ e publicar mensagens da aplicação.
/// </summary>
public sealed class RabbitMQPublisher : IRabbitMQPublisher, IDisposable
{
    private const int DefaultRabbitMqPort = 5672;
    private const string DefaultHostName = "localhost";
    private const string DefaultUserName = "admin";
    private const string DefaultPassword = "admin";
    private const string DefaultQueueName = "video_queue";
    private const string DefaultExchangeName = "video_exchange";
    private const string DefaultRoutingKey = "video_key";
    private const string DefaultDeadLetterExchange = "dlx_video_exchange";
    private const string DefaultDeadLetterQueue = "dlq_video_queue";
    private const string DefaultVirtualHost = "/";

    private readonly ILogger<RabbitMQPublisher> _logger;
    private readonly IConnectionFactory _connectionFactory;
    private readonly string _queueName;
    private readonly string _exchangeName;
    private readonly string _routingKey;
    private readonly string _deadLetterExchange;
    private readonly string _deadLetterQueue;

    private IConnection? _connection;
    private IModel? _channel;
    private bool _disposed;

    /// <summary>
    /// Inicializa uma nova instância de <see cref="RabbitMQPublisher"/> 
    /// </summary>
    public RabbitMQPublisher(ILogger<RabbitMQPublisher> logger, IConfiguration configuration)
        : this(CreateConnectionFactory(configuration), configuration, logger)
    {
    }

    /// <summary>
    /// Inicializa uma nova instância de <see cref="RabbitMQPublisher"/> permitindo injeção da factory para testes.
    /// </summary>
    public RabbitMQPublisher(
        IConnectionFactory connectionFactory,
        IConfiguration configuration,
        ILogger<RabbitMQPublisher> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        _queueName = GetConfigurationValue(configuration, "RabbitMQ:QueueName", DefaultQueueName);
        _exchangeName = GetConfigurationValue(configuration, "RabbitMQ:ExchangeName", DefaultExchangeName);
        _routingKey = GetConfigurationValue(configuration, "RabbitMQ:RoutingKey", DefaultRoutingKey);
        _deadLetterExchange = GetConfigurationValue(configuration, "RabbitMQ:DeadLetterExchange", DefaultDeadLetterExchange);
        _deadLetterQueue = GetConfigurationValue(configuration, "RabbitMQ:DeadLetterQueue", DefaultDeadLetterQueue);
    }

    /// <summary>
    /// Declara exchanges, filas e bindings necessários para o processamento assíncrono de vídeos.
    /// </summary>
    public async Task DeclareInfrastructureAsync()
    {
        try
        {
            await Task.Run(() => {
                CreateConnection();

                if (_channel is null)
                {
                    _logger.LogWarning("Canal RabbitMQ não disponível para declaração de infraestrutura.");
                    return;
                }

                _channel.ExchangeDeclare(_deadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false);

                _channel.QueueDeclare(_deadLetterQueue, durable: true, exclusive: false, autoDelete: false);
                _channel.QueueBind(_deadLetterQueue, _deadLetterExchange, _deadLetterQueue);

                _channel.ExchangeDeclare(_exchangeName, ExchangeType.Direct, durable: true, autoDelete: false);

                var queueArgs = new Dictionary<string, object>
                {
                    { "x-dead-letter-exchange", _deadLetterExchange },
                    { "x-dead-letter-routing-key", _deadLetterQueue },
                    { "x-message-ttl", 300000 },
                    { "x-max-length", 10000 },
                    { "x-overflow", "drop-head" }
                };

                _channel.QueueDeclare(
                    _queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: queueArgs
                );

                _channel.QueueBind(_queueName, _exchangeName, _routingKey);

                _logger.LogInformation(
                    "Infraestrutura RabbitMQ declarada. Exchange={Exchange}; Queue={Queue}; RoutingKey={RoutingKey}",
                    _exchangeName,
                    _queueName,
                    _routingKey
                );
            });
        } catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao declarar infraestrutura RabbitMQ: {Error}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Publica uma mensagem de vídeo em formato JSON no RabbitMQ.
    /// </summary>
    public async Task PublishVideoMessageAsync(object message)
    {
        if (message is null)
        {
            _logger.LogWarning("Mensagem nula. Publicação ignorada.");
            return;
        }

        try
        {
            await Task.Run(() => {
                CreateConnection();

                if (_channel is null)
                {
                    _logger.LogWarning("Canal RabbitMQ não disponível. Mensagem de vídeo não enviada.");
                    return;
                }

                var json = JsonSerializer.Serialize(message);
                var body = Encoding.UTF8.GetBytes(json);
                var properties = CreateBasicProperties("application/json");
                var videoId = ExtractVideoId(json);

                properties.MessageId = $"video-{videoId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
                properties.Headers = new Dictionary<string, object>
                {
                    { "source", "VideoNest" },
                    { "timestamp", DateTime.UtcNow.ToString("O") },
                    { "retry-count", 0 }
                };

                _channel.BasicPublish(_exchangeName, _routingKey, properties, body);

                _logger.LogInformation(
                    "Mensagem publicada. Exchange={Exchange}; RoutingKey={RoutingKey}; VideoId={VideoId}; Size={Size}B",
                    _exchangeName,
                    _routingKey,
                    videoId,
                    body.Length
                );
            });
        } catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Falha ao publicar mensagem: {MessageType}; Error={Error}",
                message.GetType().Name,
                ex.Message
            );

            throw;
        }
    }

    /// <summary>
    /// Publica uma mensagem textual de teste no RabbitMQ.
    /// </summary>
    public void PublishMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Mensagem obrigatória.", nameof(message));

        try
        {
            CreateConnection();

            if (_channel is null)
            {
                _logger.LogWarning("Canal RabbitMQ não disponível. Mensagem de teste não enviada.");
                return;
            }

            var body = Encoding.UTF8.GetBytes(message);
            var properties = CreateBasicProperties("text/plain");

            properties.MessageId = $"test-{DateTime.UtcNow:yyyyMMddHHmmss}";
            properties.Headers = new Dictionary<string, object>
            {
                { "source", "VideoNest-Test" },
                { "timestamp", DateTime.UtcNow.ToString("O") }
            };

            _channel.BasicPublish(_exchangeName, _routingKey, properties, body);

            _logger.LogInformation("Mensagem de teste publicada: {Message}", message);
        } catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao publicar mensagem de teste: {Error}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Libera recursos associados à conexão RabbitMQ.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        CleanupConnection();
        _disposed = true;

        GC.SuppressFinalize(this);
    }

    private void CreateConnection()
    {
        if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
            return;

        try
        {
            _channel?.Dispose();
            _connection?.Dispose();

            _connection = _connectionFactory.CreateConnection();
            _channel = _connection.CreateModel();

            _logger.LogDebug("Conexão RabbitMQ criada.");
        } catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao criar conexão RabbitMQ: {Error}", ex.Message);
            throw;
        }
    }

    private IBasicProperties CreateBasicProperties(string contentType)
    {
        if (_channel is null)
            throw new InvalidOperationException("Canal RabbitMQ não inicializado.");

        var properties = _channel.CreateBasicProperties();

        properties.Persistent = true;
        properties.ContentType = contentType;
        properties.DeliveryMode = 2;

        return properties;
    }

    private static string ExtractVideoId(string json)
    {
        try
        {
            var messageDict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

            return messageDict is not null &&
                   messageDict.TryGetValue("VideoId", out var videoIdObj) &&
                   videoIdObj is not null
                ? videoIdObj.ToString() ?? "unknown"
                : "unknown";
        } catch
        {
            return "unknown";
        }
    }

    private void CleanupConnection()
    {
        try
        {
            if (_channel is { IsOpen: true })
                _channel.Close(200, "Cleanup");

            if (_connection is { IsOpen: true })
                _connection.Close(200, "Cleanup");

            _channel?.Dispose();
            _connection?.Dispose();

            _logger.LogDebug("Conexão RabbitMQ limpa.");
        } catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro no cleanup RabbitMQ: {Error}", ex.Message);
        } finally
        {
            _channel = null;
            _connection = null;
        }
    }

    private static IConnectionFactory CreateConnectionFactory(IConfiguration configuration)
    {
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        return new ConnectionFactory {
            HostName = GetConfigurationValue(configuration, "RabbitMQ:HostName", DefaultHostName),
            Port = GetConfigurationIntValue(configuration, "RabbitMQ:Port", DefaultRabbitMqPort),
            UserName = GetConfigurationValue(configuration, "RabbitMQ:UserName", DefaultUserName),
            Password = GetConfigurationValue(configuration, "RabbitMQ:Password", DefaultPassword),
            VirtualHost = GetConfigurationValue(configuration, "RabbitMQ:VirtualHost", DefaultVirtualHost),
            AutomaticRecoveryEnabled = true,
            RequestedHeartbeat = TimeSpan.FromSeconds(60),
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
        };
    }

    private static string GetConfigurationValue(IConfiguration configuration, string key, string defaultValue)
    {
        var value = configuration[key];

        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value;
    }

    private static int GetConfigurationIntValue(IConfiguration configuration, string key, int defaultValue)
    {
        var value = configuration[key];

        return int.TryParse(value, out var parsedValue)
            ? parsedValue
            : defaultValue;
    }
}