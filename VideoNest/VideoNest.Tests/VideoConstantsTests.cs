﻿using FluentAssertions;
using VideoNest.Constants;
using Xunit;

namespace VideoNest.Tests.Constants;

public class VideoConstantsTests {
    [Fact]
    public void ValidStatuses_ShouldContainExpectedValues() {
        // Act & Assert
        VideoConstants.ValidStatuses.Should().Contain(new[] { "Na Fila", "Processando", "Concluído", "Erro" });
        VideoConstants.ValidStatuses.Should().HaveCount(4, "porque há 4 status válidos no RF6");
    }

    [Theory]
    [InlineData(".mp4", true)]
    [InlineData(".avi", true)]
    [InlineData(".jpg", false)]  // Inválido
    public void AllowedExtensions_ShouldValidateCorrectly(string extension, bool expected) {
        // Act
        var isAllowed = VideoConstants.AllowedExtensions.Contains(extension);

        // Assert
        isAllowed.Should().Be(expected);
    }

    [Fact]
    public void MaxFileSizeBytes_ShouldBe100MB() {
        // Assert (alinhado a RF1: Upload de .mp4/.avi com limite)
        VideoConstants.MaxFileSizeBytes.Should().Be(104_857_600L);
    }
}