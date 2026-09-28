using System.Text;
using EProcure.Web.Infrastructure;

namespace EProcure.Tests.Rules;

/// <summary>Uploads: PDF only (name, declared type AND real content), not empty, not over the size limit.</summary>
public class PdfValidatorTests
{
    private const long FiveMb = 5 * 1024 * 1024;
    private static readonly byte[] RealPdf = Encoding.ASCII.GetBytes("%PDF-1.7");

    private static string? Check(string name, string type, byte[] firstBytes, long length) =>
        PdfValidator.Check(name, type, firstBytes, length, FiveMb);

    [Fact]
    public void A_real_pdf_is_accepted()
    {
        Assert.Null(Check("tax-clearance.pdf", "application/pdf", RealPdf, 1024));
    }

    [Fact]
    public void Extension_and_content_type_are_not_case_sensitive()
    {
        Assert.Null(Check("CIPC.PDF", "Application/PDF", RealPdf, 1024));
    }

    [Fact]
    public void A_file_of_exactly_the_limit_is_accepted_one_byte_more_is_not()
    {
        Assert.Null(Check("a.pdf", "application/pdf", RealPdf, FiveMb));
        Assert.Contains("larger than 5 MB", Check("a.pdf", "application/pdf", RealPdf, FiveMb + 1));
    }

    [Fact]
    public void An_empty_file_is_rejected()
    {
        Assert.Equal("The file is empty.", Check("a.pdf", "application/pdf", Array.Empty<byte>(), 0));
    }

    [Theory]
    [InlineData("quote.docx", "application/pdf")]      // wrong extension
    [InlineData("quote", "application/pdf")]           // no extension
    [InlineData("quote.pdf", "application/msword")]    // wrong declared type
    [InlineData("quote.pdf", "")]
    public void Anything_not_named_and_declared_as_pdf_is_rejected(string name, string type)
    {
        Assert.Equal("Only PDF files can be uploaded.", Check(name, type, RealPdf, 1024));
    }

    [Fact]
    public void A_renamed_file_without_the_pdf_signature_is_rejected()
    {
        var textFile = Encoding.ASCII.GetBytes("This is not a PDF");
        Assert.Contains("not a valid PDF", Check("fake.pdf", "application/pdf", textFile, textFile.Length));
    }

    [Fact]
    public void A_file_shorter_than_the_signature_is_rejected()
    {
        var tiny = Encoding.ASCII.GetBytes("%P");
        Assert.Contains("not a valid PDF", Check("tiny.pdf", "application/pdf", tiny, tiny.Length));
    }
}
