using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace EProcure.Web.Services.External;

/// <summary>
/// Stores files on the local disk under App_Data/uploads/{yyyy}/{MM}/{guid}.pdf.
///
/// Security:
///  - The folder is outside wwwroot, so no file can be downloaded by guessing a URL. Files are only
///    served by a controller action that first checks who is asking (Step 7).
///  - The key is created here from a random GUID; the uploader's file name is never used as a path,
///    which rules out "path traversal" attacks such as a file named "..\..\Program.cs".
///  - Every key is checked against a strict pattern before it touches the disk.
/// </summary>
public partial class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<UploadOptions> options, IWebHostEnvironment environment)
    {
        _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.LocalRoot));
        Directory.CreateDirectory(_root);
    }

    [GeneratedRegex(@"^\d{4}/\d{2}/[0-9a-f]{32}\.pdf$")]
    private static partial Regex KeyPattern();

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct)
    {
        if (extension != ".pdf") throw new ArgumentException("Only PDF files are stored.", nameof(extension));

        var now = DateTime.UtcNow;
        var key = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}.pdf";
        var path = Resolve(key)!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        await content.CopyToAsync(file, ct);
        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        var path = Resolve(key);
        Stream? stream = path is not null && File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = Resolve(key);
        if (path is not null && File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>Turns a key into a full path, or NULL if the key is not one we could have created.</summary>
    private string? Resolve(string key)
    {
        if (!KeyPattern().IsMatch(key)) return null;
        var path = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(_root, StringComparison.OrdinalIgnoreCase) ? path : null;
    }
}
