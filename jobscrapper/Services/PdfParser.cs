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
public static class PdfParser
{
    public static ResumeInput Extract(string pdfPath)
    {
        var fullText = new StringBuilder();

        try
        {
            using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.ReadOnly);

            foreach (PdfPage page in document.Pages)
            {
                var content = ContentReader.ReadContent(page);
                var rawText = ExtractRawText(content);

                // این خط طلایی است: کاراکترهای خراب فارسی رو درست می‌کنه
                var fixedText = FixPersianGarbage(rawText);
                fullText.AppendLine(fixedText);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"خطا در خواندن PDF: {ex.Message}");
            fullText.Append("خطا در خواندن فایل PDF");
        }

        var extractedText = fullText.ToString();

        return new ResumeInput
        {
            FullText = extractedText,
            Name = ExtractName(extractedText),
            City = ExtractCity(extractedText),
            YearsExperience = ExtractExperience(extractedText),
            Skills = ExtractSkills(extractedText),
            Education = ExtractEducation(extractedText)
        };
    }

    private static string ExtractRawText(CSequence content)
    {
        var sb = new StringBuilder();
        foreach (var cObject in content)
        {
            if (cObject is COperator cOp && (cOp.OpCode.Name == "Tj" || cOp.OpCode.Name == "TJ"))
            {
                foreach (var operand in cOp.Operands)
                {
                    if (operand is CString cString)
                    {
                        sb.Append(cString.Value);
                    }
                    else if (operand is CArray cArray)
                    {
                        foreach (var item in cArray)
                            if (item is CString arrStr)
                                sb.Append(arrStr.Value);
                    }
                }
            }
        }
        return sb.ToString();
    }

    // این متد کاراکترهای خراب فارسی (مثل \u0003) رو به حروف فارسی تبدیل می‌کنه
    private static string FixPersianGarbage(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var map = new Dictionary<string, string>
        {
            // مثال برای کاراکترهای خراب رایج در PDF فارسی
            {"\u0003", " "}, {"\u0001", ""}, {"\u0002", ""}, {"\u000f", ""},
            {"\u001e", ""}, {"\u001a", ""}, {"\u0010", ""}, {"\u007f", ""},
            // تبدیل کاراکترهای خراب به فارسی (بر اساس تجربه واقعی)
            {"\uFB56", "پ"}, {"\uFB58", "ت"}, {"\uFB7A", "چ"}, {"\uFB8A", "ک"},
            {"\uFB66", "ژ"}, {"\uFB90", "گ"}, {"\u064A", "ی"}, {"\u0649", "ی"},
            {"\u0624", "ؤ"}, {"\u0626", "ئ"}, {"\u0622", "آ"}, {"\u0623", "أ"}
        };

        var result = input;
        foreach (var kvp in map)
            result = result.Replace(kvp.Key, kvp.Value, StringComparison.Ordinal);

        // حذف کاراکترهای غیرقابل چاپ
        result = Regex.Replace(result, @"\p{C}+", " ");

        return result;
    }

    private static string ExtractName(string text)
    {
        var patterns = new[]
        {
            @"نام\s*و\s*نام\s*خانوادگی\s*[:\-]?\s*([\u0600-\u06FF\s]{5,40})",
            @"نام\s*[:\-]?\s*([\u0600-\u06FF\s]{5,40})",
            @"^([\u0600-\u06FF]{3,20}\s+[\u0600-\u06FF]{3,20})"
        };

        foreach (var p in patterns)
        {
            var m = Regex.Match(text, p, RegexOptions.Multiline);
            if (m.Success && m.Groups.Count > 1)
            {
                var name = Clean(m.Groups[1].Value);
                if (name.Length >= 5 && name.Contains(" ")) return name;
            }
        }
        return "نامشخص";
    }

    private static string ExtractCity(string text)
    {
        var cities = new[] { "تهران", "مشهد", "اصفهان", "شیراز", "تبریز", "کرج", "اهواز", "قم", "کرمانشاه", "رشت", "ارومیه", "زاهدان" };
        foreach (var city in cities)
            if (text.Contains(city)) return city;
        return "نامشخص";
    }

    private static int ExtractExperience(string text)
    {
        var m = Regex.Match(text, @"(\d{1,2})\s*(سال|ساله|سال‌ها)\s*(تجربه|سابقه)", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var y) ? y : 0;
    }

    private static List<string> ExtractSkills(string text)
    {
        var skills = new[] { "پایتون", "Python", "React", "ری‌اکت", "جاوااسکریپت", "C#", ".NET", "SQL", "فوتوشاپ", "حسابداری", "پرستاری", "رانندگی", "Excel" };
        return skills.Where(s => text.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static string ExtractEducation(string text)
    {
        if (text.Contains("دکتری")) return "دکتری";
        if (text.Contains("کارشناسی ارشد") || text.Contains("فوق لیسانس")) return "کارشناسی ارشد";
        if (text.Contains("کارشناسی") || text.Contains("لیسانس")) return "کارشناسی";
        return "نامشخص";
    }

    private static string Clean(string input) => Regex.Replace(input.Trim(), @"\s+", " ");
}