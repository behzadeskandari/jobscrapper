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
                // ذخیره فایل
                using (var stream = System.IO.File.Create(tempPath))
                    await request.File.CopyToAsync(stream);

                // این خط طلایی است — خودش تشخیص می‌ده فارسی یا انگلیسی!
                var resume = PdfParser.Extract(tempPath);

                var language = resume.FullText.Any(c => c >= 0x0600 && c <= 0x06FF) ? "فارسی" : "انگلیسی";

                List<MatchResult> matches;
                if (language == "فارسی")
                {
                    matches = _inference.PredictMatchesPersian(resume, topN);
                }
                else
                {
                    matches = _inference.PredictMatchesEnglish(resume, topN);
                }

                var preview = resume.FullText.Length > 300
                    ? resume.FullText[..300] + "..."
                    : resume.FullText;

                return Ok(new
                {
                    message = "رزومه با موفقیت پردازش شد",
                    language = resume.FullText.Any(c => c >= 0x0600 && c <= 0x06FF) ? "فارسی" : "English",
                    extracted = new
                    {
                        resume.Name,
                        resume.City,
                        resume.YearsExperience,
                        resume.Education,
                        resume.Skills,
                        textPreview = preview
                    },
                    jobMatches = matches
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
