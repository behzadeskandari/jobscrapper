using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using jobscrapper.Models;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace jobscrapper.Services
{
    public static class PdfParser
    {
        public static ResumeInput Extract(string pdfPath)
        {
            var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.ReadOnly);
            var sb = new StringBuilder();

            foreach (var page in doc.Pages)
                sb.Append(page.GetText());   // PdfSharpCore extracts raw text

            var raw = sb.ToString();

            // Simple section extraction (you can improve with ML-based sectionizer)
            var skills = Regex.Match(raw, @"Skills[:\-]?\s*(.*?)(?=\n[A-Z][a-z]+:|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var experience = Regex.Match(raw, @"Experience[:\-]?\s*(.*?)(?=\n[A-Z][a-z]+:|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            return new ResumeInput
            {
                FullText = Regex.Replace(raw, @"\s+", " ").Trim(),
                Skills = skills.Success ? skills.Groups[1].Value.Trim() : ""
            };
        }

        // Helper for PdfSharpCore (requires nuget PdfSharpCore)
        private static string GetText(this PdfPage page)
        {
            var content = page.Contents;
            // Very lightweight – for production use iText7 or UglyToad.PdfPig
            return content?.ToString() ?? "";
        }
    }
}
