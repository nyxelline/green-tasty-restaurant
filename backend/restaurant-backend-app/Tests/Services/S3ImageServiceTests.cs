using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using APIHandler.Services;
using Moq;
using System.Text;

namespace APIHandler.Tests.Services;

public class S3ImageServiceTests
{
    [Theory]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("gif", "image/gif")]
    [InlineData("bmp", "image/bmp")]
    [InlineData("tiff-little", "image/tiff")]
    [InlineData("tiff-big", "image/tiff")]
    [InlineData("webp", "image/webp")]
    public async Task UploadImageAsync_UsesDetectedImageContentType(string format, string expectedContentType)
    {
        var s3Client = new Mock<IAmazonS3>();
        PutObjectRequest? uploaded = null;
        s3Client
            .Setup(client => client.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => uploaded = request)
            .ReturnsAsync(new PutObjectResponse());

        var service = new S3ImageService(s3Client.Object);
        await service.UploadImageAsync(ImageHeader(format), "images", "test-image", new Mock<ILambdaLogger>().Object);

        Assert.NotNull(uploaded);
        Assert.Equal(expectedContentType, uploaded.ContentType);
    }

    [Fact]
    public async Task UploadImageAsync_UnknownFormatDoesNotUpload()
    {
        var s3Client = new Mock<IAmazonS3>();
        var service = new S3ImageService(s3Client.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadImageAsync([0x00, 0x01, 0x02, 0x03], "images", "invalid", new Mock<ILambdaLogger>().Object));

        s3Client.Verify(client => client.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static byte[] ImageHeader(string format) => format switch
    {
        "jpeg" => [0xFF, 0xD8, 0xFF, 0xE0],
        "png" => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
        "gif" => Encoding.ASCII.GetBytes("GIF89a"),
        "bmp" => Encoding.ASCII.GetBytes("BM"),
        "tiff-little" => [0x49, 0x49, 0x2A, 0x00],
        "tiff-big" => [0x4D, 0x4D, 0x00, 0x2A],
        "webp" => [.. Encoding.ASCII.GetBytes("RIFF"), 0x00, 0x00, 0x00, 0x00, .. Encoding.ASCII.GetBytes("WEBP")],
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}