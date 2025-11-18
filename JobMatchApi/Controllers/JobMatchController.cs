using System.Text.RegularExpressions;
using JobMatchApi.Services;
using jobscrapper.Interfaces;
using jobscrapper.Models;
using jobscrapper.Services;
using Microsoft.AspNetCore.Mvc;

namespace JobMatchApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class JobMatchController : ControllerBase
    {
        private readonly IJobInferenceService _inference;

        public JobMatchController(IJobInferenceService inference)
        {
            _inference = inference;
        }

        /// <summary>
        /// Upload a PDF resume and get the best matching jobs.
        /// </summary>
        /// <param name="file">Resume PDF (max 5 MB)</param>
        /// <param name="topN">How many matches to return (default 5)</param>
        /// <returns>List of matches with probability</returns>
        [HttpPost("match")]
        [RequestSizeLimit(5_242_880)]   // 5 MB
        [ProducesResponseType(typeof(List<MatchResult>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> MatchResume(
            IFormFile file,
            [FromQuery] int topN = 5)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Only PDF files are allowed.");

            if (file.Length > 5 * 1024 * 1024)
                return BadRequest("File size exceeds 5 MB.");

            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");

            await using (var stream = System.IO.File.Create(tempPath))
            {
                await file.CopyToAsync(stream);
            }

            try
            {
                var resumeText = PdfParser.Extract(tempPath);
                if (string.IsNullOrWhiteSpace(resumeText.Name))
                    return BadRequest("Could not extract text from the PDF.");

                var resumeInput = new ResumeInput { FullText = resumeText.FullText };
                var matches = _inference.PredictMatches(resumeInput, topN);

                return Ok(matches);
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }

        /// <summary>
        /// Upload a PDF resume and get the best matching jobs with details.
        /// </summary>
        /// <param name="request">Resume upload request containing the PDF file</param>
        /// <param name="topN">How many job matches to return (default 5)</param>
        /// <returns>Resume info and list of matched jobs</returns>
        [HttpPost("match2")]
        [IgnoreAntiforgeryToken]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> MatchResume2(
           [FromForm] ResumeUploadRequest request,
           [FromQuery] int topN = 5)
        {


            if (request.File == null || request.File.Length == 0)
                return BadRequest("فایلی آپلود نشده است.");

            if (!request.File.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return BadRequest("فقط فایل PDF مجاز است.");

            var tempPath = Path.GetTempFileName() + ".pdf";

            try
            {
                await using (var stream = System.IO.File.Create(tempPath))
                {
                    await request.File.CopyToAsync(stream);
                }

                var resume = PdfParser.Extract(tempPath);

                if (string.IsNullOrWhiteSpace(resume.FullText) || resume.FullText.Length < 50)
                    return BadRequest("متن قابل خواندنی از PDF استخراج نشد.");

                // 1. دسته بندی اصلی رزومه با مدل
                var category = _inference.PredictJobCategory(resume.FullText);

                // 2. بارگذاری همه مشاغل (یکبار بهتر است این کار خارج از این متد انجام شود و داده کش شود)
                var jobsDataPath = Path.Combine(AppContext.BaseDirectory, "Data");
                var allJobs = _inference.LoadAllJobSamples(jobsDataPath);

                // 3. فیلتر کردن مشاغل فقط روی دسته‌بندی پیش‌بینی شده
                var filteredJobs = allJobs.Where(job => job.Category == category).ToList();

                // 4. مرتب سازی بر اساس تشابه ساده (تعداد کلمات مشترک)
                var resumeWords = new HashSet<string>(resume.FullText.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries));
                var rankedJobs = filteredJobs.Select(job =>
                {
                    var jobWords = new HashSet<string>(job.Question.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries));
                    int commonWordsCount = resumeWords.Intersect(jobWords).Count();
                    return new { Job = job, Score = commonWordsCount };
                })
 .OrderByDescending(x => x.Score)
 .Take(topN)
 .Select(x => x.Job)
 .ToList();

                var preview = resume.FullText.Length > 300 ? resume.FullText[..300] + "..." : resume.FullText;

                // 5. خروجی مناسب با جزئیات بیشتر
                return Ok(new
                {
                    message = "رزومه با موفقیت پردازش شد",
                    extracted = new
                    {
                        name = resume.Name,
                        city = resume.City,
                        yearsExperience = resume.YearsExperience,
                        education = resume.Education,
                        skills = resume.Skills,
                        textPreview = preview
                    },
                    jobMatches = rankedJobs.Select(job => new
                    {
                        title = job.Answer,  // معمولا عنوان شغل در Answer ذخیره میشه
                        category = job.Category,
                        description = job.Question,
                        location = "نامشخص",  // اگر داری جایگاه مکانی اضافه کن
                        link = "نامشخص"  // اگر لینک داری اضافه کن
                    })
                });
            }
            catch (Exception ex)
            {
                return Problem($"خطا در پردازش فایل: {ex.Message}");
            }
            finally
            {
                if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
            }
        }


        public static string ExtractLinkFromAnswer(string answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
                return string.Empty;

            // Regex pattern to find URLs starting with http or https
            var urlPattern = @"https?://[^\s]+";

            var match = Regex.Match(answer, urlPattern, RegexOptions.IgnoreCase);

            return match.Success ? match.Value : string.Empty;
        }
    }

}
