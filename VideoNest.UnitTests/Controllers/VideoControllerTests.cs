using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using VideoNest.Controllers;
using VideoNest.DTO;
using VideoNest.Hubs;
using VideoNest.Services;
using Xunit;

namespace VideoNest.UnitTests.Controllers;

public class VideosControllerTests {
    private readonly Mock<IVideoService> _mockVideoService;
    private readonly Mock<ILogger<VideosController>> _mockLogger;
    private readonly Mock<IHubContext<VideoHub>> _mockHubContext;
    private readonly VideosController _controller;

    public VideosControllerTests() {
        _mockVideoService = new Mock<IVideoService>();
        _mockLogger = new Mock<ILogger<VideosController>>();
        _mockHubContext = new Mock<IHubContext<VideoHub>>();
        _controller = new VideosController(_mockVideoService.Object, _mockLogger.Object, _mockHubContext.Object);
    }

    [Fact]
    public async Task UploadVideo_ValidRequest_ShouldReturnOkWithVideoId() // RF1: Upload válido
    {
        // Arrange
        var request = new VideoUploadRequest { Title = "Test Video", File = CreateMockFormFile("test.mp4", 1024) };
        var expectedVideoId = 1;
        _mockVideoService.Setup(s => s.UploadVideoAsync(It.IsAny<IFormFile>(), request)).ReturnsAsync(expectedVideoId);

        // Act
        var result = await _controller.UploadVideo(request) as OkObjectResult;

        // Assert
        result.Should().NotBeNull();
        result.StatusCode.Should().Be(200);
        var response = result.Value; // Usando var para capturar o objeto retornado
        var videoId = (int)response.GetType().GetProperty("VideoId")?.GetValue(response); // Acesso seguro via reflexão
        videoId.Should().Be(expectedVideoId);
        _mockLogger.Verify(l => l.Log(LogLevel.Information, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
    }

    [Theory]
    [InlineData(".jpg", "Formato inválido. Aceitos: .mp4, .avi")] // Edge case: Extensão inválida
    [InlineData(".mp4", "Arquivo excede o tamanho máximo de 100MB")] // Simula tamanho > 100MB, ajustado para mensagem real
    public async Task UploadVideo_InvalidFile_ShouldReturnBadRequest(string extension, string expectedError) // RF1: Validações
    {
        // Arrange
        var request = new VideoUploadRequest {
            Title = "Invalid",
            File = CreateMockFormFile($"test{extension}", extension == ".mp4" ? 105_000_000 : 1024)
        };
        _mockVideoService.Setup(s => s.UploadVideoAsync(It.IsAny<IFormFile>(), request))
            .Throws(new ArgumentException(expectedError, nameof(request.File)));

        // Act
        var result = await _controller.UploadVideo(request) as BadRequestObjectResult;

        // Assert
        result.Should().NotBeNull();
        result.StatusCode.Should().Be(400);
        var response = result.Value; // Usando var para capturar o objeto retornado
        var message = (string)response.GetType().GetProperty("Message")?.GetValue(response) ?? string.Empty; // Acesso seguro via reflexão
        message.Should().Contain(expectedError);
        _mockLogger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task UploadVideo_Exception_ShouldReturn500() // Robustez: Erro interno
    {
        // Arrange
        var request = new VideoUploadRequest { Title = "Error", File = CreateMockFormFile("test.mp4", 1024) };
        _mockVideoService.Setup(s => s.UploadVideoAsync(It.IsAny<IFormFile>(), request)).ThrowsAsync(new Exception("Erro simulado"));

        // Act
        var result = await _controller.UploadVideo(request) as ObjectResult;

        // Assert
        result.Should().NotBeNull();
        result.StatusCode.Should().Be(500);
        var response = result.Value; // Usando var para capturar o objeto retornado
        var message = (string)response.GetType().GetProperty("Message")?.GetValue(response) ?? string.Empty; // Acesso seguro via reflexão
        message.Should().Contain("Erro interno do servidor");
    }

    private IFormFile CreateMockFormFile(string fileName, long length) {
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns(fileName);
        mockFile.Setup(f => f.Length).Returns(length);
        mockFile.Setup(f => f.ContentType).Returns("video/mp4");
        return mockFile.Object;
    }
}