using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using jobscrapper.Interfaces;
using jobscrapper.Models;
using Microsoft.ML;
using Microsoft.ML.Transforms.Text;

namespace jobscrapper.Services
{
    /// <summary>
    /// ML.NET inference service for job-related Q&A.
    /// - Category classifier (one model)
    /// - One answer model per category
    /// - Saves/loads .zip models to avoid re-reading JSONL
    /// </summary>
    public class JobInferenceService : IJobInferenceService
    {
        private readonly MLContext _ml = new MLContext(seed: 42);

        // Trained models
        private ITransformer? _categoryModel;
        private PredictionEngine<JobSample, CategoryPrediction>? _catEngine;

        private readonly ConcurrentDictionary<string, ITransformer> _answerModels = new();
        private readonly ConcurrentDictionary<string, PredictionEngine<JobSample, AnswerPrediction>> _answerEngines = new();

        // ------------------------------------------------------------
        // 1. Load JSONL (Training Only)
        // ------------------------------------------------------------
        public List<JobSample> LoadAllJobSamples(string? dataFolder = null)
        {
            dataFolder ??= Path.Combine(AppContext.BaseDirectory, "Data");

            if (!Directory.Exists(dataFolder))
                throw new DirectoryNotFoundException($"Data folder not found: {dataFolder}");

            var jsonlFiles = Directory.GetFiles(dataFolder, "jobs_*.jsonl", SearchOption.TopDirectoryOnly);

            if (!jsonlFiles.Any())
                throw new FileNotFoundException($"No 'jobs_*.jsonl' files found in: {dataFolder}");

            var allSamples = new List<JobSample>();

            foreach (var file in jsonlFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var rawCategory = fileName["jobs_".Length..]; // Remove "jobs_"
                var category = rawCategory
                    .Replace("__", " - ")   // Replace double underscore with dash
                    .Replace("_", " ");     // Single underscore → space

                Console.WriteLine($"Loading: {Path.GetFileName(file)} → Category: {category}");

                var samples = File.ReadLines(file)
                    .Select((line, index) =>
                    {
                        if (string.IsNullOrWhiteSpace(line)) return null;
                        try
                        {
                            var sample = System.Text.Json.JsonSerializer.Deserialize<JobSample>(line,
                                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (sample != null)
                                sample.Category = category; // Force correct Persian category

                            return sample;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error parsing line {index + 1} in {Path.GetFileName(file)}: {ex.Message}");
                            return null;
                        }
                    })
                    .Where(s => s != null)
                    .Cast<JobSample>()
                    .ToList();

                allSamples.AddRange(samples);
            }

            Console.WriteLine($"Successfully loaded {allSamples.Count} samples from {jsonlFiles.Length} files.");
            return allSamples;
        }

        // ------------------------------------------------------------
        // 2. Train Category Classifier
        // ------------------------------------------------------------
        public void TrainCategoryModel(List<JobSample> data)
        {

            var ml = new MLContext();

            var dataView = ml.Data.LoadFromEnumerable(data);

            var pipeline = ml.Transforms.Text.FeaturizeText(
                                outputColumnName: "Features",
                                inputColumnName: nameof(JobSample.Question))
                            .Append(ml.Transforms.Conversion.MapValueToKey("Label", nameof(JobSample.Category)))
                            .Append(ml.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                                featureColumnName: "Features",
                                labelColumnName: "Label"))
                            .Append(ml.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            var model = pipeline.Fit(dataView);

            _catEngine = ml.Model.CreatePredictionEngine<JobSample, CategoryPrediction>(model);

            Console.WriteLine("Model trained successfully (multiclass text classifier).");
        } // ------------------------------------------------------------
        // 3. Train Answer Models (One per Category)
        // ------------------------------------------------------------
        public void TrainAnswerModels(List<JobSample> data)
        {
            if (data == null || !data.Any())
                throw new ArgumentException("No data to train answer models.");

            var categories = data.Select(x => x.Category).Distinct().ToList();
            if (!categories.Any())
                throw new InvalidOperationException("No categories found in training data.");

            Parallel.ForEach(categories, cat =>
            {
                var catData = data.Where(x => x.Category == cat).ToList();
                if (!catData.Any()) return;

                var dv = _ml.Data.LoadFromEnumerable(catData);

                var pipeline = _ml.Transforms.Text
                        .FeaturizeText("Features", nameof(JobSample.Question))
                    .Append(_ml.Transforms.Conversion.MapValueToKey("Label", nameof(JobSample.Answer)))
                    .Append(_ml.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
                    .Append(_ml.Transforms.Conversion.MapKeyToValue("PredictedLabel", "PredictedLabel"));

                var model = pipeline.Fit(dv);
                var normCat = Normalize(cat);

                _answerModels[normCat] = model;
                _answerEngines[normCat] = _ml.Model.CreatePredictionEngine<JobSample, AnswerPrediction>(model);

                Console.WriteLine($"Trained answer model for category: {cat}");
            });
        }

        // ------------------------------------------------------------
        // 4. Save Models to Disk
        // ------------------------------------------------------------
        public void SaveCategoryModel(string path)
        {
            if (_categoryModel == null)
                throw new InvalidOperationException("Category model not trained.");

            var dummy = _ml.Data.LoadFromEnumerable(new[] { new JobSample() });
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _ml.Model.Save(_categoryModel, dummy.Schema, path);
            Console.WriteLine($"Category model saved to: {path}");
        }

        public void SaveAnswerModels(string folder)
        {
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            foreach (var kv in _answerModels)
            {
                var path = Path.Combine(folder, $"answer_{kv.Key}.zip");
                var dummy = _ml.Data.LoadFromEnumerable(new[] { new JobSample() });
                _ml.Model.Save(kv.Value, dummy.Schema, path);
                Console.WriteLine($"Answer model saved: {path}");
            }
        }

        // ← NEW: Combined save method
        public void SaveCategoryAndAnswerModels(string folder)
        {
            SaveCategoryModel(Path.Combine(folder, "category.zip"));
            SaveAnswerModels(folder);
            Console.WriteLine($"All models saved to folder: {folder}");
        }

        // ------------------------------------------------------------
        // 5. Load Models from Disk
        // ------------------------------------------------------------
        public void LoadCategoryModel(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Category model not found: {path}");

            var model = _ml.Model.Load(path, out var schema);
            _categoryModel = model;
            _catEngine = _ml.Model.CreatePredictionEngine<JobSample, CategoryPrediction>(model);
            Console.WriteLine($"Category model loaded from: {path}");
        }

        public void LoadAnswerModels(string folder)
        {
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException($"Models folder not found: {folder}");

            var files = Directory.GetFiles(folder, "answer_*.zip");
            if (!files.Any())
                throw new FileNotFoundException($"No answer models found in: {folder}");

            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var cat = fileName["answer_".Length..]; // strip "answer_" prefix
                var model = _ml.Model.Load(file, out _);
                var engine = _ml.Model.CreatePredictionEngine<JobSample, AnswerPrediction>(model);

                _answerEngines[cat] = engine;
                Console.WriteLine($"Answer model loaded: {fileName}.zip");
            }
        }

        // ------------------------------------------------------------
        // 6. Prediction Using Category Engine
        // ------------------------------------------------------------
        public string PredictJobCategory(string question)
        {
            if (_catEngine == null)
                throw new InvalidOperationException("Category model not loaded.");

            var input = new JobSample { Question = question };
            var prediction = _catEngine.Predict(input);
            return prediction.PredictedLabel ?? "unknown";
        }

        public string PredictAnswer(string question)
        {
            var category = PredictJobCategory(question);
            var normCat = Normalize(category);

            if (!_answerEngines.TryGetValue(normCat, out var engine))
                return $"No answer model for category: {category}";

            var input = new JobSample { Question = question };
            var prediction = engine.Predict(input);
            return prediction.PredictedLabel ?? "no answer found";
        }

        // ← NEW: For your resume-matching endpoint
        public List<MatchResult> PredictMatches(ResumeInput resume, int topN)
        {
            // 1. Use category engine to predict job category from resume
            var category = PredictJobCategory(resume.FullText);

            // 2. Find matching jobs based on skills/experience (your custom logic)
            var matches = new List<MatchResult>(); // Replace with real matching

            // Example: Simple string matching (replace with BERT/ML)
            foreach (var skill in resume.Skills.Split(','))
            {
                matches.Add(new MatchResult
                {
                    JobTitle = $"Job with {skill.Trim()} skill",
                    Company = "Example Corp",
                    Category = category,
                    MatchScore = 0.85f,
                    HiringProbability = "85%"
                });
            }

            return matches.Take(topN).ToList();
        }

        // ------------------------------------------------------------
        // Utility
        // ------------------------------------------------------------
        private static string Normalize(string s) =>
            s.Replace(" ", "_")
             .Replace("-", "_")
             .Replace("/", "_")
             .Trim()
             .ToLowerInvariant();
    }

}
