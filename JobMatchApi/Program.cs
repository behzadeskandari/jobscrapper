
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
app.UseAuthorization();
app.MapControllers();


app.Run();