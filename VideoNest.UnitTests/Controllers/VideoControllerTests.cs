using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Reflection;
using System.Threading.Tasks;
using VideoNest.Controllers;
using VideoNest.DTO;
using VideoNest.Hubs;
using VideoNest.Services;
using Xunit;

namespace VideoNest.UnitTests.Controllers;

public class VideosControllerTests
{
    private readonly Mock<IVideoService> _mockVideoService;
    private readonly Mock<ILogger<VideosController>> _mockLogger;
    private readonly Mock<IHubContext<VideoHub>> _mockHubContext;
    private readonly VideosController _controller;

    public VideosControllerTests()
    {
        _mockVideoService = new Mock<IVideoService>();
        _mockLogger = new Mock<ILogger<VideosController>>();
        _mockHubContext = new Mock<IHubContext<VideoHub>>();

        _controller = new VideosController(
            _mockVideoService.Object,
            _mockLogger.Object,
            _mockHubContext.Object
        );
    }

    [Fact]
    public async Task UploadVideo_ValidRequest_ShouldReturnOkWithVideoId()
    {
        var request = new VideoUploadRequest {
            Title = "Test Video",
            File = CreateMockFormFile("test.mp4", 1024)
        };

        const int expectedVideoId = 1;

        _mockVideoService
            .Setup(s => s.UploadVideoAsync(It.IsAny<IFormFile>(), request))
            .ReturnsAsync(expectedVideoId);

        var actionResult = await _controller.UploadVideo(request);

        var result = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status200OK);

        var videoId = GetRequiredPropertyValue<int>(result.Value, "VideoId");
        videoId.Should().Be(expectedVideoId);

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

    [Theory]
    [InlineData(".jpg", "Formato inválido. Aceitos: .mp4, .avi")]
    [InlineData(".mp4", "Arquivo excede o tamanho máximo de 100MB")]
    public async Task UploadVideo_InvalidFile_ShouldReturnBadRequest(string extension, string expectedError)
    {
        var request = new VideoUploadRequest {
            Title = "Invalid",
            File = CreateMockFormFile($"test{extension}", extension == ".mp4" ? 105_000_000 : 1024)
        };

        _mockVideoService
            .Setup(s => s.UploadVideoAsync(It.IsAny<IFormFile>(), request))
            .ThrowsAsync(new ArgumentException(expectedError, nameof(request.File)));

        var actionResult = await _controller.UploadVideo(request);

        var result = actionResult.Should().BeOfType<BadRequestObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        var message = GetRequiredPropertyValue<string>(result.Value, "Message");
        message.Should().Contain(expectedError);

        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task UploadVideo_Exception_ShouldReturn500()
    {
        var request = new VideoUploadRequest {
            Title = "Error",
            File = CreateMockFormFile("test.mp4", 1024)
        };

        _mockVideoService
            .Setup(s => s.UploadVideoAsync(It.IsAny<IFormFile>(), request))
            .ThrowsAsync(new Exception("Erro simulado"));

        var actionResult = await _controller.UploadVideo(request);

        var result = actionResult.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        var message = GetRequiredPropertyValue<string>(result.Value, "Message");
        message.Should().Contain("Erro interno do servidor");
    }

    private static IFormFile CreateMockFormFile(string fileName, long length)
    {
        var mockFile = new Mock<IFormFile>();

        mockFile.Setup(f => f.FileName).Returns(fileName);
        mockFile.Setup(f => f.Length).Returns(length);
        mockFile.Setup(f => f.ContentType).Returns("video/mp4");

        return mockFile.Object;
    }

    private static T GetRequiredPropertyValue<T>(object? source, string propertyName)
    {
        source.Should().NotBeNull();

        var property = source!
            .GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

        property.Should().NotBeNull();

        var value = property!.GetValue(source);
        value.Should().NotBeNull();

        return value.Should().BeAssignableTo<T>().Subject;
    }
}