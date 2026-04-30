using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using VideoNest.Services;
using Xunit;

namespace VideoNest.UnitTests.Messaging;

public class RabbitMQPublisherTests
{
    private readonly Mock<IConnectionFactory> _mockConnectionFactory;
    private readonly Mock<IConnection> _mockConnection;
    private readonly Mock<IModel> _mockChannel;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<RabbitMQPublisher>> _mockLogger;
    private readonly RabbitMQPublisher _publisher;

    public RabbitMQPublisherTests()
    {
        _mockConnectionFactory = new Mock<IConnectionFactory>();
        _mockConnection = new Mock<IConnection>();
        _mockChannel = new Mock<IModel>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<RabbitMQPublisher>>();

        _mockConnectionFactory
            .Setup(f => f.CreateConnection())
            .Returns(_mockConnection.Object);

        _mockConnection
            .Setup(c => c.CreateModel())
            .Returns(_mockChannel.Object);

        _mockConfiguration.Setup(c => c["RabbitMQ:QueueName"]).Returns("video_queue");
        _mockConfiguration.Setup(c => c["RabbitMQ:ExchangeName"]).Returns("video_exchange");
        _mockConfiguration.Setup(c => c["RabbitMQ:RoutingKey"]).Returns("video_key");
        _mockConfiguration.Setup(c => c["RabbitMQ:DeadLetterExchange"]).Returns("dlx_video_exchange");
        _mockConfiguration.Setup(c => c["RabbitMQ:DeadLetterQueue"]).Returns("dlq_video_queue");
        _mockConfiguration.Setup(c => c["RabbitMQ:HostName"]).Returns("rabbitmq_hackathon");
        _mockConfiguration.Setup(c => c["RabbitMQ:Port"]).Returns("5672");
        _mockConfiguration.Setup(c => c["RabbitMQ:UserName"]).Returns("admin");
        _mockConfiguration.Setup(c => c["RabbitMQ:Password"]).Returns("admin");
        _mockConfiguration.Setup(c => c["RabbitMQ:VirtualHost"]).Returns("/");

        _publisher = new RabbitMQPublisher(
            _mockConnectionFactory.Object,
            _mockConfiguration.Object,
            _mockLogger.Object
        );
    }

    [Fact]
    public async Task DeclareInfrastructureAsync_ShouldCreateDLQ()
    {
        await _publisher.DeclareInfrastructureAsync();

        _mockChannel.Verify(
            c => c.ExchangeDeclare("dlx_video_exchange", "direct", true, false, null),
            Times.Once
        );

        _mockChannel.Verify(
            c => c.QueueDeclare("dlq_video_queue", true, false, false, It.IsAny<Dictionary<string, object>>()),
            Times.Once
        );

        _mockChannel.Verify(
            c => c.QueueBind("dlq_video_queue", "dlx_video_exchange", "dlq_video_queue", null),
            Times.Once
        );

        _mockChannel.Verify(
            c => c.ExchangeDeclare("video_exchange", "direct", true, false, null),
            Times.Once
        );

        _mockChannel.Verify(
            c => c.QueueDeclare("video_queue", true, false, false, It.IsAny<Dictionary<string, object>>()),
            Times.Once
        );

        _mockChannel.Verify(
            c => c.QueueBind("video_queue", "video_exchange", "video_key", null),
            Times.Once
        );

        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ),
            Times.AtLeastOnce
        );
    }

    [Fact]
    public void PublishMessage_ShouldPublishTestMessage()
    {
        var message = "Test Message";
        var expectedBody = Encoding.UTF8.GetBytes(message);
        var mockProperties = new Mock<IBasicProperties>();

        var propertiesConfigured = false;
        ReadOnlyMemory<byte> publishedBody = ReadOnlyMemory<byte>.Empty;
        IBasicProperties? preparedProperties = null;

        _mockChannel
            .Setup(c => c.CreateBasicProperties())
            .Callback(() => {
                propertiesConfigured = true;

                mockProperties.SetupProperty(p => p.Persistent);
                mockProperties.SetupProperty(p => p.ContentType);
                mockProperties.SetupProperty(p => p.DeliveryMode);
                mockProperties.SetupProperty(p => p.MessageId);
                mockProperties.SetupProperty(p => p.Headers);

                preparedProperties = mockProperties.Object;
            })
            .Returns(mockProperties.Object);

        _mockChannel
            .Setup(c => c.BasicPublish(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<IBasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>()
            ))
            .Callback<string, string, bool, IBasicProperties, ReadOnlyMemory<byte>>(
                (_, _, _, _, body) => publishedBody = body
            );

        _publisher.PublishMessage(message);

        propertiesConfigured.Should().BeTrue();

        preparedProperties.Should().NotBeNull();
        preparedProperties!.ContentType.Should().Be("text/plain");
        preparedProperties.DeliveryMode.Should().Be(2);
        preparedProperties.MessageId.Should().StartWith("test-");
        preparedProperties.Headers.Should().NotBeNull();
        preparedProperties.Headers!["source"].Should().Be("VideoNest-Test");

        publishedBody.ToArray().Should().Equal(expectedBody);

        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ),
            Times.AtLeastOnce
        );
    }

    [Fact]
    public async Task DeclareInfrastructureAsync_Exception_ShouldLogError()
    {
        _mockConnectionFactory
            .Setup(f => f.CreateConnection())
            .Throws(new InvalidOperationException("Erro simulado"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _publisher.DeclareInfrastructureAsync());

        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ),
            Times.AtLeastOnce
        );
    }
}