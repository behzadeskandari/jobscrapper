

//using jobscrapper.Models;
//using jobscrapper.Services;

//var builder = WebApplication.CreateBuilder(args);

//// Add services
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

//// Load jobs ONCE at startup
//var jobs = JobRepository.Load("jobs.csv"); // Put jobs.csv in wwwroot or root
//var jobMatcher = new JobMatcher(jobs);

//// Register as singleton
//builder.Services.AddSingleton(jobMatcher);

//var app = builder.Build();

//// Configure pipeline
//if (app.Environment.IsDevelopment())
//{
//    app.UseSwagger();
//    app.UseSwaggerUI();
//}

//app.UseHttpsRedirection();

//// API Endpoint
//app.MapPost("/api/resume/match", async (IFormFile resumeFile, JobMatcher matcher, int topN = 5) =>
//{
//    if (resumeFile == null || resumeFile.Length == 0)
//        return Results.BadRequest("No file uploaded.");

//    if (!resumeFile.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
//        return Results.BadRequest("Only PDF files are allowed.");

//    // Save temp file
//    var tempPath = Path.GetTempFileName() + ".pdf";
//    using (var stream = File.Create(tempPath))
//        await resumeFile.CopyToAsync(stream);

//    // Parse resume
//    var resume = PdfParser.Extract(tempPath);

//    // Clean up
//    File.Delete(tempPath);

//    // Predict
//    var matches = matcher.Predict(resume, topN);

//    return Results.Ok(new
//    {
//        resumeSkills = resume.Skills.Length > 100 ? resume.Skills.Substring(0, 100) + "..." : resume.Skills,
//        topMatches = matches
//    });
//})
//.WithName("MatchResume")
//.WithOpenApi()
//.Accepts<IFormFile>("multipart/form-data");

//app.Run();


// Program.cs
using CsvHelper;
using jobscrapper;
using jobscrapper.Interfaces;
using jobscrapper.Models;
using jobscrapper.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ML;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "JobScrapper API", Version = "v1" });
});

// Register heavy services as SINGLETONS
builder.Services.AddSingleton<JobPredictor>(sp =>
{
    // Models are in ./Models (BERT + ML.NET .zip files)
    return new JobPredictor(modelFolder: "Models");
});

// --------------------------------------------------
// 1. Register services (singletons = one instance for the whole app)
// --------------------------------------------------
builder.Services.AddSingleton<IDataLoader, CsvDataLoader>();
builder.Services.AddSingleton<IJobInferenceService, JobInferenceService>();
builder.Services.AddSingleton<MedicalModelService>();   // your wrapper/predictor

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// --------------------------------------------------
// 2. Load ALL models **once** at application start
// --------------------------------------------------
await using (var scope = app.Services.CreateAsyncScope())
{
    var inference = scope.ServiceProvider.GetRequiredService<ITextInferenceService>();

    // Paths – change only if you move the folder
    var basePath = Path.Combine(AppContext.BaseDirectory, "models");

    // 1. Category classifier
    var catPath = Path.Combine(basePath, "category_classifier.zip");
    inference.LoadCategoryModel(catPath);

    // 2. All doctor-specific answer models
    inference.LoadAllAnswerModels(basePath);

    Console.WriteLine("All models loaded successfully!");
}

// Swagger in dev
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    // OR specify endpoint explicitly
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "JobScrapper API V1");
        c.RoutePrefix = "swagger"; // Makes Swagger UI at root: https://localhost:7001/
    });
}

app.UseHttpsRedirection();
app.MapPost("/api/resume/match", async (
    IFormFile file,
    JobPredictor predictor,
    [FromQuery] int topN = 5) =>
{
    // 1. Validate
    if (file == null || file.Length == 0)
        return Results.BadRequest("No file uploaded.");

    if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("Only PDF files allowed.");

    if (file.Length > 5 * 1024 * 1024)
        return Results.BadRequest("File too large (max 5 MB).");

    var tempPath = Path.GetTempFileName() + ".pdf";
    try
    {
        // ---- 1. Save uploaded PDF -------------------------------------------------
        using (var stream = File.Create(tempPath))
            await file.CopyToAsync(stream);

        // ---- 2. Parse the PDF → ResumeInput (all fields filled) -----------------
        var resume = PdfParser.Extract(tempPath);          // ← returns ResumeInput
        if (string.IsNullOrWhiteSpace(resume.FullText))
            return Results.BadRequest("Failed to extract any text from the PDF.");

        // ---- 3. Run the ML predictor (expects ResumeInput) ----------------------
        var matches = predictor.Predict(resume, topN);

        // ---- 4. Build a short preview of the *raw* text -------------------------
        var preview = resume.FullText.Length > 200
            ? resume.FullText[..200] + "..."
            : resume.FullText;

        // ---- 5. Return everything the front-end needs -------------------------
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
        return Results.Problem(
            detail: "Error processing resume: " + ex.Message,
            statusCode: 500);
    }
    finally
    {
        if (File.Exists(tempPath))
            File.Delete(tempPath);
    }
    //try
    //{
    //    using (var stream = File.Create(tempPath))
    //        await file.CopyToAsync(stream);

    //    // 2. Extract raw text (string)
    //    var resumeText = PdfParser.Extract(tempPath);  // ← returns string
    //    if (string.IsNullOrWhiteSpace(resumeText))
    //        return Results.BadRequest("Failed to extract text from PDF.");

    //    // 3. Create input for predictor
    //    var resumeInput = new ResumeInput { FullText = resumeText };

    //    // 4. Predict
    //    var matches = predictor.Predict(resumeInput, topN);

    //    // 5. Return preview of **raw extracted text**, not ResumeInput
    //    var preview = resumeText.Length > 200
    //        ? resumeText[..200] + "..."
    //        : resumeText;

    //    return Results.Ok(new
    //    {
    //        extractedTextPreview = preview,
    //        topMatches = matches
    //    });
    //}
    //catch (Exception ex)
    //{
    //    return Results.Problem(
    //        detail: "Error processing resume: " + ex.Message,
    //        statusCode: 500);
    //}
    //finally
    //{
    //    if (File.Exists(tempPath))
    //        File.Delete(tempPath);
    //}



})
.WithName("MatchResume")
//.WithOpenApi()
.Accepts<IFormFile>("multipart/form-data")
.Produces<object>(200)
.ProducesProblem(500);


app.Run();