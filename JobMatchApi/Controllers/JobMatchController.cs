using jobscrapper.Models;
using jobscrapper.Services;
using Microsoft.AspNetCore.Mvc;

namespace JobMatchApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class JobMatchController : ControllerBase
    {
        private readonly JobPredictor _predictor;

        // DI – predictor is a singleton (models are heavy)
        public JobMatchController(JobPredictor predictor)
        {
            _predictor = predictor;
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
        public IActionResult MatchResume(
            IFormFile file,
            [FromQuery] int topN = 5)
        {
            // ---- 1. Validate file -------------------------------------------------
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Only PDF files are allowed.");

            if (file.Length > 5 * 1024 * 1024)
                return BadRequest("File size exceeds 5 MB.");

            // ---- 2. Save to temp location -----------------------------------------
            var tempPath = Path.GetTempFileName() + ".pdf";
            using (var stream = System.IO.File.Create(tempPath))
            {
                file.CopyTo(stream);
            }

            try
            {
                // ---- 3. Extract text from PDF ------------------------------------
                var resumeText = PdfParser.Extract(tempPath);   // <-- your existing helper
                if (string.IsNullOrWhiteSpace(resumeText.Name))
                    return BadRequest("Could not extract text from the PDF.");

                // ---- 4. Predict ----------------------------------------------------
                var resumeInput = new ResumeInput { FullText = resumeText.FullText };
                var matches = _predictor.Predict(resumeInput, topN);

                return Ok(matches);
            }
            finally
            {
                // ---- 5. Clean up ----------------------------------------------------
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }
    }
}
