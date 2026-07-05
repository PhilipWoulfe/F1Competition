using F1.Web.Models;
using F1.Web.Services.Api;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;

namespace F1.Web.Tests.Services.Api;

public class RaceMetadataApiServiceTests
{
    private const string RaceId = "main-2025-24-abu-dhabi-grand-prix";

    [Fact]
    public async Task GetPublishedAsync_WhenResponseIsSuccess_ReturnsMetadata()
    {
        // Arrange
        var metadata = new RaceQuestionMetadata
        {
            RaceId = RaceId,
            H2HQuestion = "Who finishes higher?",
            BonusQuestion = "How many safety cars?",
            IsPublished = true,
            UpdatedAtUtc = new DateTime(2025, 12, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(metadata), System.Text.Encoding.UTF8, "application/json")
        };

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("http://localhost") };
        var service = new RaceMetadataApiService(httpClient);

        // Act
        var result = await service.GetPublishedAsync(RaceId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(RaceId, result.RaceId);
        Assert.Equal("Who finishes higher?", result.H2HQuestion);
        Assert.True(result.IsPublished);
    }

    [Fact]
    public async Task GetPublishedAsync_WhenResponseIsNotFound_ReturnsNull()
    {
        // Arrange
        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.NotFound
        };

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("http://localhost") };
        var service = new RaceMetadataApiService(httpClient);

        // Act
        var result = await service.GetPublishedAsync(RaceId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPublishedAsync_WhenResponseIsServerError_ThrowsApiServiceException()
    {
        // Arrange
        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.InternalServerError,
            Content = new StringContent("Server error", System.Text.Encoding.UTF8, "text/plain")
        };

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("http://localhost") };
        var service = new RaceMetadataApiService(httpClient);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiServiceException>(() => service.GetPublishedAsync(RaceId));
        Assert.Equal(HttpStatusCode.InternalServerError, ex.Error.StatusCode);
    }

    [Fact]
    public async Task GetPublishedAsync_WhenRaceIdIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("http://localhost") };
        var service = new RaceMetadataApiService(httpClient);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetPublishedAsync(""));
    }
}
