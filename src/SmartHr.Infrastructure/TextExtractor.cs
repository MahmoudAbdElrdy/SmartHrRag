using System.Text;
using DocumentFormat.OpenXml.Packaging;
using SmartHr.Application;
using UglyToad.PdfPig;

namespace SmartHr.Infrastructure;

public sealed class TextExtractor : ITextExtractor
{
    public string Extract(string filename, Stream stream)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        if (ext is ".txt" or ".md")
        {
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true
            );
            return reader.ReadToEnd();
        }
        if (ext == ".pdf")
        {
            using var pdf = PdfDocument.Open(stream);
            return string.Join("\n\n", pdf.GetPages().Select(p => p.Text));
        }
        if (ext == ".docx")
        {
            using var doc = WordprocessingDocument.Open(stream, false);
            return doc.MainDocumentPart?.Document?.Body?.InnerText ?? "";
        }
        throw new NotSupportedException("Supported: .txt, .md, .pdf, .docx");
    }
}
