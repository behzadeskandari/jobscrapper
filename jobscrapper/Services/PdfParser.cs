//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;
//using jobscrapper.Models;
//using PdfSharpCore.Pdf;
//using PdfSharpCore.Pdf.IO;

//namespace jobscrapper.Services
//{
//    public static class PdfParser
//    {
//        public static ResumeInput Extract(string pdfPath)
//        {
//            var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.ReadOnly);
//            var sb = new StringBuilder();

//            foreach (var page in doc.Pages)
//                sb.Append(page.GetText());   // PdfSharpCore extracts raw text

//            var raw = sb.ToString();

//            // Simple section extraction (you can improve with ML-based sectionizer)
//            var skills = Regex.Match(raw, @"Skills[:\-]?\s*(.*?)(?=\n[A-Z][a-z]+:|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
//            var experience = Regex.Match(raw, @"Experience[:\-]?\s*(.*?)(?=\n[A-Z][a-z]+:|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

//            return new ResumeInput
//            {
//                FullText = Regex.Replace(raw, @"\s+", " ").Trim(),
//                Skills = skills.Success ? skills.Groups[1].Value.Trim() : ""
//            };
//        }

//        // Helper for PdfSharpCore (requires nuget PdfSharpCore)
//        private static string GetText(this PdfPage page)
//        {
//            var content = page.Contents;
//            // Very lightweight – for production use iText7 or UglyToad.PdfPig
//            return content?.ToString() ?? "";
//        }
//    }
//}
using System.Text;
using System.Text.RegularExpressions;
using jobscrapper.Models;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Content;
using PdfSharpCore.Pdf.Content.Objects;
using PdfSharpCore.Pdf.IO;

public static partial class PdfParser
{
    public static ResumeInput Extract(string pdfPath)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            throw new FileNotFoundException($"PDF not found: {pdfPath}");

        var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.ReadOnly);
        var sb = new StringBuilder();

        foreach (PdfPage page in doc.Pages)
            sb.Append(page.GetText());

        string raw = sb.ToString();
        string clean = CleanText(raw);

        var sections = ExtractSections(clean);
        string name = ExtractName(clean);
        int years = ExtractYearsExperience(clean);
        string city = ExtractCity(clean);

        return new ResumeInput
        {
            FullText = clean,
            Name = name,
            Education = sections.GetValueOrDefault("Education", ""),
            YearsExperience = years,
            City = city,
            Skills = sections.GetValueOrDefault("Skills", "")
        };
    }

    // --------------------------------------------------------------
    // 1. Clean text
    // --------------------------------------------------------------
    private static string CleanText(string input) =>
        Regex.Replace(input, @"\s+", " ").Trim();

    // --------------------------------------------------------------
    // 2. Extract Name (first line or "Name:" pattern)
    // --------------------------------------------------------------
    private static string ExtractName(string text)
    {
        // Try: "Name: John Doe"
        var m = Regex.Match(text, @"Name\s*[:\-]?\s*([A-Za-z\s\.]+)", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.Trim();

        // Fallback: first non-empty line (often the name)
        var firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .FirstOrDefault();
        return firstLine?.Trim() ?? "";
    }

    // --------------------------------------------------------------
    // 3. Extract Years of Experience
    // --------------------------------------------------------------
    private static int ExtractYearsExperience(string text)
    {
        // Match: "5 years", "3+ years", "10 yrs", "8-year experience"
        var patterns = new[]
        {
                @"\b(\d{1,2})\+?\s*(?:years?|yrs?)\b",
                @"(\d{1,2})\-year",
                @"(\d{1,2})\s*years?\s*(?:of\s*)?experience"
            };

        foreach (var p in patterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int years))
                return years;
        }
        return 0;
    }

    // --------------------------------------------------------------
    // 4. Extract City (from address or "Location: Berlin")
    // --------------------------------------------------------------
    private static string ExtractCity(string text)
    {
        // Try: "Location: Berlin", "City: Munich"
        var m = Regex.Match(text, @"(?:Location|City)[:\-]?\s*([A-Za-z\s\-]+)", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.Trim();

        // Fallback: look for German cities in text
        var cities = new[] { "Berlin", "Munich", "Hamburg", "Cologne", "Frankfurt", "Stuttgart", "Düsseldorf", "Dortmund", "Essen", "Leipzig" };
        foreach (var city in cities)
            if (text.Contains(city, StringComparison.OrdinalIgnoreCase))
                return city;

        return "";
    }

    // --------------------------------------------------------------
    // 5. Extract Sections (Skills, Education, etc.)
    // --------------------------------------------------------------
    private static Dictionary<string, string> ExtractSections(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sections = new[] { "Skills", "Education", "Experience", "Summary" };

        foreach (var sec in sections)
        {
            var pattern = $@"{Regex.Escape(sec)}\s*[:\-]?\s*(.*?)(?=\n[A-Z]{{2,}}[:\-]|$)";
            var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            result[sec] = m.Success
                ? m.Groups[1].Value.Replace("\n", " ").Trim()
                : "";
        }
        return result;
    }

    // --------------------------------------------------------------
    // 6. Safe page text extraction
    // --------------------------------------------------------------
    private static string GetText(this PdfPage page)
    {
        try
        {
            // Read the content stream of the page
            var content = ContentReader.ReadContent(page);
            if (content == null) return "";

            var text = new StringBuilder();

            // Iterate through all content objects (CObject)
            foreach (var cObject in content)
            {
                if (cObject is COperator op)
                {
                    // Look for text-showing operators: Tj, TJ, ', "
                    if (op.OpCode.Name is "Tj" or "TJ" or "'" or "\"")
                    {
                        // The operand before the operator is the text
                        if (op.Operands.Count > 0 && op.Operands[0] is CString cString)
                        {
                            text.Append(cString.Value);
                        }
                        else if (op.Operands.Count > 0 && op.Operands[0] is CArray cArray)
                        {
                            // TJ operator may contain array of strings and spacing
                            foreach (var item in cArray)
                            {
                                if (item is CString str)
                                    text.Append(str.Value);
                                // Ignore numbers (kerning/spacing)
                            }
                        }
                    }
                }
            }

            return text.ToString();
        }
        catch
        {
            return "";
        }
    }
}