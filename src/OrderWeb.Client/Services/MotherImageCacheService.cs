using System.Security.Cryptography;
using OrderWeb.SharedUI.Assets;

namespace OrderWeb.Client.Services;

/// <summary>
/// Downloads Mother-owned images into the Client app-data folder. SQLite keeps
/// metadata only; no image blobs are stored in the database.
/// </summary>
public sealed class MotherImageCacheService
{
    private const int MaxImageBytes = 10 * 1024 * 1024;
    private const int MaxImageDimension = 4096;
    private const long MaxImagePixels = 16_000_000;
    private readonly ClientCacheService _cache;

    public MotherImageCacheService() : this(new ClientCacheService()) { }
    public MotherImageCacheService(ClientCacheService cache) => _cache = cache;

    public event EventHandler<CachedImageMetadata>? ImageUpdated;

    public async Task<ImageSource> GetOrRefreshAsync(MotherImageDescriptor image, CancellationToken cancellationToken = default)
    {
        var existing = await _cache.GetImageMetadataAsync(image.ImageId);
        if (existing != null && existing.ContentHash.Equals(image.ContentHash, StringComparison.OrdinalIgnoreCase) && File.Exists(existing.LocalPath))
        {
            return ImageSource.FromFile(existing.LocalPath);
        }

        var settings = await _cache.GetMotherConnectionAsync();
        var session = await _cache.GetCurrentLoginSessionAsync();
        if (settings == null || session == null)
        {
            return Placeholder();
        }

        string? temporary = null;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            ClientCompatibilityHeaders.Apply(client);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", session.SessionToken);
            using var response = await client.GetAsync(ToAbsoluteUrl(settings.ApiBaseUrl, image.RemotePath), cancellationToken);
            if (!response.IsSuccessStatusCode || !IsImageContentType(response.Content.Headers.ContentType?.MediaType))
            {
                return Placeholder();
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0 || bytes.Length > MaxImageBytes || !TryGetImageDimensions(bytes, out var width, out var height) ||
                width <= 0 || height <= 0 || width > MaxImageDimension || height > MaxImageDimension || (long)width * height > MaxImagePixels)
            {
                return Placeholder();
            }

            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var expectedHash = string.IsNullOrWhiteSpace(image.ContentHash)
                ? response.Headers.TryGetValues("X-Image-Hash", out var hashes) ? hashes.FirstOrDefault() : null
                : image.ContentHash;
            if (string.IsNullOrWhiteSpace(expectedHash) || !hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return Placeholder();
            }

            var directory = Path.Combine(FileSystem.AppDataDirectory, "mother-images");
            Directory.CreateDirectory(directory);
            var extension = ExtensionFor(response.Content.Headers.ContentType?.MediaType);
            var safeName = string.Concat(image.ImageId.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
            var destination = Path.Combine(directory, $"{safeName}_{hash[..12]}{extension}");
            temporary = destination + ".tmp";
            await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await file.WriteAsync(bytes, cancellationToken);
                await file.FlushAsync(cancellationToken);
            }
            File.Move(temporary, destination, overwrite: true);

            var metadata = new CachedImageMetadata(image.ImageId, image.RemotePath, hash, destination,
                response.Content.Headers.ContentType?.MediaType, DateTimeOffset.UtcNow.ToString("O"));
            await _cache.SaveImageMetadataAsync(metadata);
            if (existing != null && !string.Equals(existing.LocalPath, destination, StringComparison.OrdinalIgnoreCase) && File.Exists(existing.LocalPath))
            {
                File.Delete(existing.LocalPath);
            }
            ImageUpdated?.Invoke(this, metadata);
            return ImageSource.FromFile(destination);
        }
        catch (HttpRequestException) { return Placeholder(); }
        catch (TaskCanceledException) { return Placeholder(); }
        catch (IOException) { return Placeholder(); }
        finally
        {
            try { if (!string.IsNullOrWhiteSpace(temporary) && File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
        }
    }

    private static string ToAbsoluteUrl(string baseUrl, string remotePath) =>
        remotePath.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? remotePath : $"{baseUrl.TrimEnd('/')}/{remotePath.TrimStart('/')}";
    private static bool IsImageContentType(string? value) => value?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
    private static bool TryGetImageDimensions(byte[] b, out int width, out int height)
    {
        width = height = 0;
        if (b.Length >= 24 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
        {
            width = ReadBigEndian(b, 16); height = ReadBigEndian(b, 20); return true;
        }
        if (b.Length >= 10 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46)
        {
            width = b[6] | b[7] << 8; height = b[8] | b[9] << 8; return true;
        }
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            for (var i = 2; i + 9 < b.Length;)
            {
                if (b[i++] != 0xFF) continue;
                while (i < b.Length && b[i] == 0xFF) i++;
                if (i >= b.Length) break;
                var marker = b[i++];
                if (marker is 0xD8 or 0xD9) continue;
                if (i + 1 >= b.Length) break;
                var length = (b[i] << 8) | b[i + 1];
                if (length < 2 || i + length > b.Length) break;
                if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF)
                {
                    height = (b[i + 3] << 8) | b[i + 4]; width = (b[i + 5] << 8) | b[i + 6]; return true;
                }
                i += length;
            }
        }
        return false;
    }
    private static int ReadBigEndian(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    private static string ExtensionFor(string? mime) => mime?.ToLowerInvariant() switch { "image/png" => ".png", "image/gif" => ".gif", _ => ".jpg" };
    private static ImageSource Placeholder() => ImageSource.FromFile(SharedImageNames.DefaultFood);
}

public sealed record MotherImageDescriptor(string ImageId, string RemotePath, string ContentHash);
