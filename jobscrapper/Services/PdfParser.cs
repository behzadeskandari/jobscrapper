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
using jobscrapper;
using jobscrapper.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace JobMatchApi.Services
{

    public static class PdfParser
    {
        public static ResumeInput Extract(string pdfPath)
        {
            var rawText = ExtractRawText(pdfPath);
            var language = DetectLanguage(rawText);

            return language == Language.Persian
                ? ExtractPersianResume(rawText)
                : ExtractEnglishResume(rawText);
        }

        private static string ExtractRawText(string pdfPath)
        {
            var sb = new StringBuilder();
            byte[] pdfBytes = File.ReadAllBytes(pdfPath);
            using var ms = new MemoryStream(pdfBytes);
            using var pdf = PdfDocument.Open(ms);
            foreach (var page in pdf.GetPages())
            {
                var text = ContentOrderTextExtractor.GetText(page);
                if (!string.IsNullOrWhiteSpace(text))
                    sb.AppendLine(text);
            }
            return sb.ToString();
        }

        private enum Language { Persian, English }

        private static Language DetectLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Language.English;

            int persianChars = text.Count(c => c >= 0x0600 && c <= 0x06FF);
            int englishChars = text.Count(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'));

            // اگر بیش از 15 کاراکتر فارسی داشت → فارسی
            // اگر بیش از 100 کاراکتر انگلیسی داشت و فارسی کم بود → انگلیسی
            return (persianChars > 15) || (persianChars > englishChars * 0.3)
                ? Language.Persian
                : Language.English;
        }

        private static ResumeInput ExtractPersianResume(string rawText)
        {
            var fixedText = FixPersianTextDirections.FixPersianTextDirection(rawText);
            var cleanedText = CleanPersianText(fixedText);

            return new ResumeInput
            {
                FullText = cleanedText,
                Name = ExtractPersianName(cleanedText) ?? "نامشخص",
                City = ExtractPersianCity(cleanedText) ?? "نامشخص",
                YearsExperience = ExtractPersianExperience(cleanedText),
                Skills = ExtractPersianSkills(cleanedText),
                Education = ExtractPersianEducation(cleanedText) ?? "نامشخص"
            };
        }

        private static ResumeInput ExtractEnglishResume(string rawText)
        {
            var cleanedText = CleanEnglishText(rawText);

            return new ResumeInput
            {
                FullText = cleanedText,
                Name = ExtractEnglishName(cleanedText) ?? "Unknown",
                City = ExtractEnglishCity(cleanedText) ?? "Unknown",
                YearsExperience = ExtractEnglishExperience(cleanedText),
                Skills = ExtractEnglishSkills(cleanedText),
                Education = ExtractEnglishEducation(cleanedText) ?? "Unknown"
            };
        }

        // ====================== فارسی ======================
        private static string CleanPersianText(string input) => CleanText(input, isPersian: true);
        private static string CleanEnglishText(string input) => CleanText(input, isPersian: false);

        private static string CleanText(string input, bool isPersian)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var text = input
                .Replace("‌", " ").Replace("\u200C", " ").Replace("\u200B", "")
                .Replace("\r\n", "\n").Replace("\r", "\n");

            if (isPersian)
            {
                text = text
                    .Replace("۰", "0").Replace("۱", "1").Replace("۲", "2").Replace("۳", "3").Replace("۴", "4")
                    .Replace("۵", "5").Replace("۶", "6").Replace("۷", "7").Replace("۸", "8").Replace("۹", "9")
                    .Replace("ي", "ی").Replace("ك", "ک").Replace("ة", "ه").Replace("ۀ", "ه");
            }

            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static string ExtractPersianName(string text) => ExtractName(text, isPersian: true);
        private static string ExtractEnglishName(string text) => ExtractName(text, isPersian: false);

        private static string ExtractName(string text, bool isPersian)
        {
            var patterns = isPersian
                ? new[] { @"نام\s*(?:و\s*نام\s*خانوادگی)?\s*[:\-]?\s*([^\n\r]{5,60})", @"^([\u0600-\u06FF]{2,}\s+[\u0600-\u06FF]{2,})" }
                : new[] { @"Name[:\-]?\s*([A-Za-z\s]{5,60})", @"^[A-Z][a-z]+(?:\s[A-Z][a-z]+)+" };

            foreach (var p in patterns)
            {
                var m = Regex.Match(text, p, RegexOptions.Multiline);
                if (m.Success && m.Groups[1].Success)
                {
                    var name = m.Groups[1].Value.Trim();
                    if (name.Length > 4 && name.Length < 60 && !name.Contains("@"))
                        return name;
                }
            }
            return null;
        }

        private static string ExtractPersianCity(string text)
        {
            var cities = new[] { "تهران", "مشهد", "اصفهان", "شیراز", "تبریز", "کرج", "اهواز", "قم", "کرمانشاه", "رشت", "ارومیه", "زاهدان", "همدان", "یزد", "اردبیل", "بندرعباس", "اراک", "ایلام", "بجنورد", "بوشهر", "شهرکرد", "بیرجند", "گرگان", "ساری", "سنندج", "قزوین", "کرمان", "خرم‌آباد", "سمنان", "زنجان", "یاسوج" };
            return cities.FirstOrDefault(text.Contains) ?? null;
        }

        private static string ExtractEnglishCity(string text)
        {
            var cities = new[] { "Tehran", "Mashhad", "Isfahan", "Shiraz", "Tabriz", "Karaj", "Ahvaz", "Qom", "London", "New York", "Berlin", "Toronto", "Dubai", "Istanbul" };
            return cities.FirstOrDefault(c => text.Contains(c, StringComparison.OrdinalIgnoreCase)) ?? null;
        }

        private static int ExtractPersianExperience(string text)
        {
            var m = Regex.Match(text, @"(\d+|۰|۱|۲|۳|۴|۵|۶|۷|۸|۹)+?\s*(?:سال|years?)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var num = Regex.Replace(m.Value, @"[^\d۰-۹]", "");
                num = num.Replace("۰", "0").Replace("۱", "1").Replace("۲", "2").Replace("۳", "3").Replace("۴", "4")
                         .Replace("۵", "5").Replace("۶", "6").Replace("۷", "7").Replace("۸", "8").Replace("۹", "9");
                if (int.TryParse(num, out int y) && y < 50) return y;
            }
            return 0;
        }

        private static int ExtractEnglishExperience(string text)
        {
            var m = Regex.Match(text, @"(\d+)\+?\s*(?:years?|yrs?)", RegexOptions.IgnoreCase);
            return m.Success && int.TryParse(m.Groups[1].Value, out int y) && y < 50 ? y : 0;
        }

        private static List<string> ExtractPersianSkills(string text)
        {
            var skills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var block = Regex.Match(text, @"مهارت(?:\s*ها|‌ها)?\s*[:\-]?\s*([\s\S]+?)(?:\n\n|تحصیلات|Experience)", RegexOptions.IgnoreCase);
            if (block.Success)
            {
                skills.UnionWith(block.Groups[1].Value
                    .Split(new[] { ',', '،', '\n', ';', '/', '|', '•' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 1 && s.Length < 50));
            }
            if (!skills.Any())
                skills.UnionWith(new[] { "React", "جاوااسکریپت", ".NET", "SQL", "پایتون", "Git", "Docker" }.Where(s => text.Contains(s)));

            return skills.Take(20).ToList();
        }

        private static List<string> ExtractEnglishSkills(string text)
        {
            var skills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var block = Regex.Match(text, @"Skills?[:\-]?\s*([\s\S]+?)(?:\n\n|Education|Experience)", RegexOptions.IgnoreCase);
            if (block.Success)
            {
                skills.UnionWith(block.Groups[1].Value
                    .Split(new[] { ',', '\n', ';', '/', '|', '•' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 1 && s.Length < 50));
            }
            if (!skills.Any())
                skills.UnionWith(new[] { "React", "JavaScript", ".NET", "Python", "SQL", "AWS", "Docker", "Git" }.Where(s => text.Contains(s, StringComparison.OrdinalIgnoreCase)));

            return skills.Take(20).ToList();
        }

        private static string ExtractPersianEducation(string text)
        {
            if (text.Contains("دکتری") || text.Contains("PhD")) return "دکتری";
            if (text.Contains("کارشناسی ارشد") || text.Contains("Master")) return "کارشناسی ارشد";
            if (text.Contains("کارشناسی") || text.Contains("Bachelor")) return "کارشناسی";
            return "نامشخص";
        }

        private static string ExtractEnglishEducation(string text)
        {
            if (text.Contains("PhD") || text.Contains("Doctor")) return "PhD";
            if (text.Contains("Master") || text.Contains("MSc")) return "Master's Degree";
            if (text.Contains("Bachelor") || text.Contains("BSc")) return "Bachelor's Degree";
            return "Unknown";
        }
    }


}
