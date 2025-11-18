
using CsvHelper;
using jobscrapper;
using jobscrapper.Interfaces;
using jobscrapper.Models;
using jobscrapper.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.ML;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "JobScrapper API", Version = "v1" });
});

// این خط طلایی است — تمام مشکل رو حل می‌کنه!
builder.Services.AddAntiforgery();
builder.Services.AddScoped<SkipAntiforgeryFilter>();
// Job-specific services
builder.Services.AddSingleton<IJobDataLoader, JsonlDataLoader>();
builder.Services.AddSingleton<IJobInferenceService, JobInferenceService>();

var app = builder.Build();

// ... بقیه کدت مثل قبل (بارگذاری مدل‌ها و ...)

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "JobScrapper API v1");
        c.RoutePrefix = "swagger";
        c.DocumentTitle = "JobScrapper AI - Persian Resume Matcher";
        c.DefaultModelsExpandDepth(-1);
    });
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAntiforgery();     // الان کاملاً بی‌ضرره
app.UseAuthorization();
app.MapControllers();

// Endpoint — دقیقاً همون قبلی، بدون هیچ میدلور و فیلتر اضافه!
app.MapPost("/api/resume/match", async (
    [FromForm] IFormFile file,
    [FromQuery] int topN = 5) =>
{
    var inference = app.Services.GetRequiredService<IJobInferenceService>();

    if (file == null || file.Length == 0)
        return Results.BadRequest("فایلی آپلود نشده است.");

    if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("فقط فایل PDF مجاز است.");

    if (file.Length > 10 * 1024 * 1024)
        return Results.BadRequest("حجم فایل بیش از حد مجاز است (حداکثر 10 مگابایت).");

    var tempPath = Path.GetTempFileName() + ".pdf";

    try
    {
        using var stream = File.Create(tempPath);
        await file.CopyToAsync(stream);

        var resume = PdfParser.Extract(tempPath);

        if (string.IsNullOrWhiteSpace(resume.FullText) || resume.FullText.Length < 50)
            return Results.BadRequest("متن قابل خواندنی از PDF استخراج نشد.");

        var matches = inference.PredictMatches(resume, topN);

        var preview = resume.FullText.Length > 300
            ? resume.FullText[..300] + "..."
            : resume.FullText;

        return Results.Ok(new
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
            jobMatches = matches
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"خطا در پردازش فایل: {ex.Message}");
    }
    finally
    {
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }
})
.AddEndpointFilter<SkipAntiforgeryFilter>()  // This skips validation
.WithName("MatchResume")
.WithOpenApi()
.Accepts<IFormFile>("multipart/form-data")
.Produces<object>(200);

app.Run();