
using CsvHelper;
using jobscrapper;
using jobscrapper.Interfaces;
using jobscrapper.Models;
using jobscrapper.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ML;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
// ---------- Services ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "JobScrapper API", Version = "v1" });
});

// Job-specific services (singletons = load once)
builder.Services.AddSingleton<IJobDataLoader, JsonlDataLoader>();  // Loads JSONL
builder.Services.AddSingleton<IJobInferenceService, JobInferenceService>();  // ML.NET inference

var app = builder.Build();

// ---------- Command-line args ----------
var argsList = args.ToList();
var isTrainMode = argsList.Contains("train");

// ---------- 1. Load data/models at startup ----------
await using (var scope = app.Services.CreateAsyncScope())
{
    var inference = scope.ServiceProvider.GetRequiredService<IJobInferenceService>();
    var baseDir = AppContext.BaseDirectory;
    var dataFolder = Path.Combine(baseDir, "Data");
    var modelsFolder = Path.Combine(baseDir, "Models");
    var catModelPath = Path.Combine(modelsFolder, "category_classifier.zip");  // Changed to match save method

    Directory.CreateDirectory(modelsFolder);

    if (!File.Exists(catModelPath))
    {
        Console.WriteLine("Training mode: Loading all Persian JSONL files...");
        var allData = inference.LoadAllJobSamples(dataFolder);  // Now loads 9600 samples!
        inference.TrainCategoryModel(allData);
        inference.TrainPerCategoryModels(allData);  // New: Trains model_*.zip
        inference.SaveCategoryAndPerCategoryModels(modelsFolder);  // Combined save
        Console.WriteLine("All models trained and saved!");
    }
    else
    {
        Console.WriteLine("Inference mode: Loading saved models...");
        inference.LoadCategoryModel(catModelPath);
        inference.LoadPerCategoryModels(modelsFolder);  // New: Loads model_*.zip
        Console.WriteLine("Ready for resume matching!");
    }
}
// ---------- 2. Middleware ----------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "JobScrapper API v1");
        c.RoutePrefix = "swagger";
        c.DocumentTitle = "JobScrapper AI - Persian Resume Matcher";
        c.DefaultModelsExpandDepth(-1); // مخفی کردن schemaها برای تمیزی
    });
}

app.UseHttpsRedirection();
app.MapControllers();       
app.UseRouting();
// ---------- 3. API Endpoint ----------
app.MapPost("/api/resume/match", async (
        HttpContext context,
        IFormFile file,
        [FromQuery] int topN = 5) =>
{
    var inference = context.RequestServices.GetRequiredService<IJobInferenceService>();

    if (file == null || file.Length == 0)
        return Results.BadRequest("No file uploaded.");

    if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("Only PDF files allowed.");

    if (file.Length > 5 * 1024 * 1024)
        return Results.BadRequest("File too large (max 5 MB).");

    var tempPath = Path.GetTempFileName() + ".pdf";

    try
    {
        using (var stream = File.Create(tempPath))
            await file.CopyToAsync(stream);

        var resume = PdfParser.Extract(tempPath);
        if (string.IsNullOrWhiteSpace(resume.FullText))
            return Results.BadRequest("Failed to extract text from PDF.");

        var matches = inference.PredictMatches(resume, topN);

        var preview = resume.FullText.Length > 200
            ? resume.FullText[..200] + "..."
            : resume.FullText;

        return Results.Ok(new
        {
            extractedTextPreview = preview,
            name = resume.Name,
            city = resume.City,
            yearsExperience = resume.YearsExperience,
            skills = resume.Skills,
            education = resume.Education,
            topMatches = matches
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
    finally
    {
        if (File.Exists(tempPath))
            File.Delete(tempPath);
    }
})
.WithName("MatchResume")
.WithOpenApi(operation => new(operation)
{
    Summary = "Upload a resume (PDF) and get best job matches using AI",
    Description = "Extracts text from Persian PDF resumes and returns top job matches based on trained ML models.",
    Tags = new[] { new OpenApiTag { Name = "Resume Matching" } }
})
.Accepts<IFormFile>("multipart/form-data")
.Produces<object>(200)
.ProducesProblem(400)
.ProducesProblem(500);
app.Run();