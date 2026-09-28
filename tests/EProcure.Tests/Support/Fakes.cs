using System.Text;
using EProcure.Web.Services.External;
using Microsoft.AspNetCore.Http;

namespace EProcure.Tests.Support;

/// <summary>Keeps "uploaded" files in memory instead of on disk.</summary>
public sealed class InMemoryFileStorage : IFileStorage
{
    public Dictionary<string, byte[]> Files { get; } = new();

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, ct);
        var key = Guid.NewGuid().ToString("N") + extension;
        Files[key] = copy.ToArray();
        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) =>
        Task.FromResult<Stream?>(Files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        Files.Remove(key);
        return Task.CompletedTask;
    }
}

public static class Upload
{
    /// <summary>A small but genuine-looking PDF (starts with the "%PDF-" signature).</summary>
    public static IFormFile Pdf(string name = "document.pdf") =>
        File(name, "application/pdf", "%PDF-1.4\n1 0 obj << >> endobj\n%%EOF");

    public static IFormFile File(string name, string contentType, string content)
    {
        var bytes = Encoding.ASCII.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
