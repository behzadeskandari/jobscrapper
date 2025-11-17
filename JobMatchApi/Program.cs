//var builder = WebApplication.CreateBuilder(args);

//// Add services to the container.
//// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
//builder.Services.AddOpenApi();

//var app = builder.Build();

//// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.MapOpenApi();
//}

//app.UseHttpsRedirection();

//var summaries = new[]
//{
//    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
//};

//app.MapGet("/weatherforecast", () =>
//{
//    var forecast =  Enumerable.Range(1, 5).Select(index =>
//        new WeatherForecast
//        (
//            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
//            Random.Shared.Next(-20, 55),
//            summaries[Random.Shared.Next(summaries.Length)]
//        ))
//        .ToArray();
//    return forecast;
//})
//.WithName("GetWeatherForecast");

//app.Run();

//record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
//{
//    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
//}
// JobMatchApi/Program.cs

using jobscrapper.Models;
using jobscrapper.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Load jobs ONCE at startup
var jobs = JobRepository.Load("jobs.csv"); // Put jobs.csv in wwwroot or root
var jobMatcher = new JobMatcher(jobs);

// Register as singleton
builder.Services.AddSingleton(jobMatcher);

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// API Endpoint
app.MapPost("/api/resume/match", async (IFormFile resumeFile, JobMatcher matcher, int topN = 5) =>
{
    if (resumeFile == null || resumeFile.Length == 0)
        return Results.BadRequest("No file uploaded.");

    if (!resumeFile.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("Only PDF files are allowed.");

    // Save temp file
    var tempPath = Path.GetTempFileName() + ".pdf";
    using (var stream = File.Create(tempPath))
        await resumeFile.CopyToAsync(stream);

    // Parse resume
    var resume = PdfParser.Extract(tempPath);

    // Clean up
    File.Delete(tempPath);
    
    // Predict
    var matches = matcher.Predict(resume, topN);

    return Results.Ok(new
    {
        resumeSkills = resume.Skills.Length > 100 ? resume.Skills.Substring(0, 100) + "..." : resume.Skills,
        topMatches = matches
    });
})
.WithName("MatchResume")
.WithOpenApi()
.Accepts<IFormFile>("multipart/form-data");

app.Run();
