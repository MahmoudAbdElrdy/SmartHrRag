using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
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
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                return "";
            }

            var builder = new StringBuilder();
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                var text = paragraph.InnerText.Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
                if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
                    || style.StartsWith("Title", StringComparison.OrdinalIgnoreCase))
                {
                    var level = int.TryParse(style.AsSpan(style.Length - 1), out var n) ? Math.Clamp(n, 1, 6) : 1;
                    builder.Append(new string('#', level)).Append(' ');
                }

                builder.Append(text).Append("\n\n");
            }

            return builder.ToString();
        }

        throw new NotSupportedException("Supported: .txt, .md, .pdf, .docx");
    }
}
