using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using VideoNest.DTO;
using Xunit;

namespace VideoNest.UnitTests.DTO;

public class VideoUploadRequestTests {
    [Fact]
    public void TryValidate_Valid_ShouldReturnTrue() // Validação customizada
    {
        // Arrange
        var request = new VideoUploadRequest { Title = "Valid", File = CreateMockFormFile("test.mp4", 1024) };

        // Act
        var isValid = request.TryValidate(out var error);

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "Arquivo de vídeo é obrigatório e deve ter conteúdo")] // Ajustado para mensagem real
    [InlineData("test.txt", "Formato inválido. Aceitos: .mp4, .avi")] // Ajustado para mensagem real
    public void TryValidate_Invalid_ShouldReturnFalseWithError(string fileName, string expectedError) {
        // Arrange
        var request = new VideoUploadRequest { File = fileName == null ? null : CreateMockFormFile(fileName, 1024) };

        // Act
        var isValid = request.TryValidate(out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().Be(expectedError); // Usando .Be() para igualdade exata
    }

    private IFormFile CreateMockFormFile(string fileName, long length) {
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns(fileName);
        mockFile.Setup(f => f.Length).Returns(length);
        return mockFile.Object;
    }
}