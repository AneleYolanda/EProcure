namespace EProcure.Web.Infrastructure;

/// <summary>
/// Decides whether an uploaded file is an acceptable PDF. Three separate checks, because each one alone
/// can be faked:
///   1. the name ends in ".pdf"                         (a renamed .exe would pass this alone)
///   2. the browser says it is "application/pdf"        (the browser can be told anything)
///   3. the file really starts with the PDF signature "%PDF-"   (the one check that looks at the content)
/// plus a size limit (5 MB by default) and "not empty". A pure function: easy to unit-test (Step 8).
/// </summary>
public static class PdfValidator
{
    private static readonly byte[] Signature = "%PDF-"u8.ToArray();

    /// <returns>NULL if the file is acceptable, otherwise the message to show the supplier.</returns>
    public static string? Check(string? fileName, string? contentType, ReadOnlySpan<byte> firstBytes, long length, long maxBytes)
    {
        if (length == 0) return "The file is empty.";
        if (length > maxBytes) return $"The file is larger than {maxBytes / (1024 * 1024)} MB. Please upload a smaller PDF.";
        if (!string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            return "Only PDF files can be uploaded.";
        if (!string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return "Only PDF files can be uploaded.";
        if (firstBytes.Length < Signature.Length || !firstBytes[..Signature.Length].SequenceEqual(Signature))
            return "This file is not a valid PDF. Save or export it as a PDF and try again.";
        return null;
    }
}
