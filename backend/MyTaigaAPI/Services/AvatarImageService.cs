using SkiaSharp;

namespace MyTaigaAPI.Services;

public class AvatarImageService
{
    public const long MaxFileBytes = 5 * 1024 * 1024;

    public async Task<byte[]> ConvertToPngAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file.Length <= 0)
            throw new ArgumentException("File ảnh rỗng.");

        if (file.Length > MaxFileBytes)
            throw new ArgumentException("Ảnh đại diện tối đa 5 MB.");

        using var input = new MemoryStream();

        await file.CopyToAsync(input, cancellationToken);

        if (input.Length > MaxFileBytes)
            throw new ArgumentException("Ảnh đại diện tối đa 5 MB.");

        input.Position = 0;

        using var codec = SKCodec.Create(input);

        if (codec is null)
            throw new ArgumentException("Không đọc được nội dung ảnh.");

        if (codec.EncodedFormat != SKEncodedImageFormat.Jpeg
            && codec.EncodedFormat != SKEncodedImageFormat.Png)
        {
            throw new ArgumentException("Chỉ chấp nhận ảnh JPEG hoặc PNG.");
        }

        var width = codec.Info.Width;
        var height = codec.Info.Height;

        // Giới hạn bộ nhớ khi giải mã ảnh.
        if (width <= 0 || height <= 0
            || width > 4096 || height > 4096
            || (long)width * height > 16_000_000)
        {
            throw new ArgumentException(
                "Ảnh tối đa 4096 pixel mỗi chiều và 16 triệu pixel.");
        }

        var imageInfo = new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var bitmap = new SKBitmap(imageInfo);

        var result = codec.GetPixels(imageInfo, bitmap.GetPixels());

        if (result != SKCodecResult.Success)
            throw new ArgumentException("Ảnh bị lỗi hoặc không đầy đủ.");

        cancellationToken.ThrowIfCancellationRequested();

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        if (encoded is null)
            throw new InvalidOperationException("Không thể xuất ảnh PNG.");

        return encoded.ToArray();
    }
}