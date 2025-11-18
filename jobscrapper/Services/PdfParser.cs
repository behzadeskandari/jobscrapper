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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using jobscrapper.Models;

namespace JobMatchApi.Services
{

        public static class PdfParser
        {
            /// <summary>
            /// Extracts the entire text from a PDF and parses structured resume fields.
            /// </summary>
            public static ResumeInput Extract(string pdfPath)
            {
                var sb = new StringBuilder();

                // Read all bytes first to avoid file locking issues
                byte[] pdfBytes = File.ReadAllBytes(pdfPath);

                using (var ms = new MemoryStream(pdfBytes))
                using (var pdf = PdfDocument.Open(ms))
                {
                    foreach (var page in pdf.GetPages())
                    {
                        // Extract text best suited for Persian/Arabic + RTL PDFs
                        var text = ContentOrderTextExtractor.GetText(page);

                        if (!string.IsNullOrWhiteSpace(text))
                            sb.AppendLine(text);
                    }
                }

                var fullText = CleanText(sb.ToString());

                return new ResumeInput
                {
                    FullText = fullText,
                    Name = ExtractName(fullText) ?? "نامشخص",
                    City = ExtractCity(fullText) ?? "نامشخص",
                    YearsExperience = ExtractYearsExperience(fullText) ?? 0,
                    Skills = ExtractSkills(fullText),
                    Education = ExtractEducation(fullText) ?? "نامشخص"
                };
            }

            // Normalize text (Persian digits, characters, whitespace)
            private static string CleanText(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                    return string.Empty;

                var output = input;

                // Normalize Persian digits
                output = output
                    .Replace("۰", "0").Replace("۱", "1").Replace("۲", "2")
                    .Replace("۳", "3").Replace("۴", "4").Replace("۵", "5")
                    .Replace("۶", "6").Replace("۷", "7").Replace("۸", "8")
                    .Replace("۹", "9");

                // Normalize Arabic/Persian characters
                output = output
                    .Replace("ي", "ی")
                    .Replace("ك", "ک")
                    .Replace("ۀ", "ه")
                    .Replace("ة", "ه");

                // Replace multiple spaces and trim
                output = Regex.Replace(output, @"[ \t]+", " ");
                output = Regex.Replace(output, @"\n{3,}", "\n\n");

                return output.Trim();
            }

            // Extract name heuristically from typical Persian resume fields
            private static string ExtractName(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                var patterns = new[]
                {
                @"نام(?: و نام خانوادگی)?:\s*(.+)",
                @"نام:\s*(.+)",
                @"نام و نام خانوادگی:\s*(.+)"
            };

                foreach (var p in patterns)
                {
                    var match = Regex.Match(text, p, RegexOptions.Multiline);
                    if (match.Success)
                        return match.Groups[1].Value.Trim();
                }

                // Fallback: first plausible line (2-4 words, no digits, no emails)
                var lines = text.Split('\n')
                                .Select(l => l.Trim())
                                .Where(l => l.Count(c => char.IsWhiteSpace(c)) >= 1 &&
                                            l.Length < 60 &&
                                            !l.Contains("@") &&
                                            !Regex.IsMatch(l, @"\d"))
                                .ToList();

                return lines.FirstOrDefault();
            }

            // Extract city from typical Persian keywords or common city names
            private static string ExtractCity(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                var patterns = new[]
                {
                @"شهر:\s*(.+)",
                @"محل سکونت:\s*(.+)",
                @"استان:\s*(.+)",
                @"ساکن\s+(.+)"
            };

                foreach (var p in patterns)
                {
                    var match = Regex.Match(text, p);
                    if (match.Success)
                        return match.Groups[1].Value.Trim();
                }

                // Common Iranian cities fallback
                var cities = new[]
                {
                "تهران","شیراز","اصفهان","تبریز","کرج","مشهد","اهواز",
                "یزد","کرمان","سنندج","زنجان","قزوین","قم","رشت","ساری"
            };

                foreach (var c in cities)
                {
                    if (text.Contains(c))
                        return c;
                }

                return null;
            }

            // Extract years of experience (look for numbers followed by 'سال')
            private static int? ExtractYearsExperience(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                var match = Regex.Match(text, @"(\d+)\s*سال", RegexOptions.Multiline);

                if (match.Success && int.TryParse(match.Groups[1].Value, out int years))
                    return years;

                return null;
            }

            // Extract skill list by detecting "مهارت‌ها" section and splitting
            private static List<string> ExtractSkills(string text)
            {
                var skills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var match = Regex.Match(text, @"مهارت(?: ها|‌ها)?:([\s\S]+?)(?:\n\n|\r\n\r\n|تحصیلات|سوابق|Skills|Education)", RegexOptions.Multiline);

                if (match.Success)
                {
                    var block = match.Groups[1].Value;

                    var items = block
                        .Split(new[] { ',', '،', '\n', '/', '|' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 1);

                    foreach (var item in items)
                        skills.Add(item);
                }

                return skills.ToList();
            }

            // Extract education degree from common Persian keywords
            private static string ExtractEducation(string text)
            {
                var eduPatterns = new[]
                {
                @"تحصیلات(?:.+?)(کاردانی|کارشناسی|کارشناسی ارشد|دکترا)",
                @"مدرک:\s*(.+)",
                @"Education:\s*(.+)"
            };

                foreach (var p in eduPatterns)
                {
                    var match = Regex.Match(text, p, RegexOptions.Multiline);
                    if (match.Success)
                        return match.Groups[1].Value.Trim();
                }

                return null;
            }
        }



}
