using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using jobscrapper.Interfaces;
using jobscrapper.Models;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms;
using Microsoft.ML.Transforms.Text;
using PdfSharpCore.Pdf.Filters;

namespace jobscrapper.Services
{
    public class JobInferenceService : IJobInferenceService
    {
        private readonly MLContext _ml = new MLContext(seed: 42);
        private List<JobSample> _allJobSamples;
        // Category classifier
        private ITransformer? _categoryModel;
        private PredictionEngine<JobSample, CategoryPrediction>? _catEngine;

        // Per-category answer models
        private readonly ConcurrentDictionary<string, ITransformer> _perCategoryModels = new();
        private readonly ConcurrentDictionary<string, PredictionEngine<JobSample, AnswerPrediction>> _perCategoryEngines = new();
        private readonly ConcurrentDictionary<string, ITransformer> _answerModels = new();
        private readonly ConcurrentDictionary<string, PredictionEngine<JobSample, AnswerPrediction>> _answerEngines = new();
        // ------------------------------------------------------------
        // 1. Load All Persian JSONL Files (Maps JobText to Question)
        // ------------------------------------------------------------

        public JobInferenceService()
        {
            var modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "category_classifier.zip");
            if (File.Exists(modelPath))
                LoadCategoryModel(modelPath);
            else
            {
                var samples = LoadAllJobSamples();
                TrainCategoryModel(samples);
                SaveCategoryModel(modelPath);
            }
            _allJobSamples = LoadAllJobSamples();
        }
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
                var rawCategory = fileName["jobs_".Length..];
                var category = rawCategory
                    .Replace("__", " - ")
                    .Replace("_", " ");

                Console.WriteLine($"Loading: {Path.GetFileName(file)} → Category: {category}");

                var lines = File.ReadLines(file);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        // Deserialize to dict (no class change needed)
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(line,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (dict == null) continue;

                        // Map JobText + Title to Question (your fix!)
                        var jobText = dict.GetValueOrDefault("JobText", "") ?? "";
                        var title = dict.GetValueOrDefault("Title", "") ?? "";
                        var question = $"{title} {jobText}".Trim();

                        if (question.Length < 10) continue;  // Skip too short

                        allSamples.Add(new JobSample
                        {
                            Question = question,
                            Category = category,
                            Answer = title  // Or use dict["ShortDescription"] for per-category training
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error parsing line in {Path.GetFileName(file)}: {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"Loaded {allSamples.Count} samples from {jsonlFiles.Length} files.");
            return allSamples;
        }

        // ------------------------------------------------------------
        // 2. Train Category Model (category_classifier.zip)
        // ------------------------------------------------------------
        public void TrainCategoryModel(List<JobSample> data)
        {
            if (data == null || !data.Any())
                throw new ArgumentException("Training data is empty.");

            var cleanData = data.Where(s =>
                !string.IsNullOrWhiteSpace(s.Question) &&
                !string.IsNullOrWhiteSpace(s.Category))
                .ToList();

            if (!cleanData.Any())
                throw new InvalidOperationException("No valid data for training.");

            Console.WriteLine($"Training category model on {cleanData.Count} samples ({cleanData.Select(x => x.Category).Distinct().Count()} categories).");

            var dataView = _ml.Data.LoadFromEnumerable(cleanData);

            var pipeline = _ml.Transforms.Text
                .NormalizeText(
                    outputColumnName: "QuestionNorm",
                    inputColumnName: nameof(JobSample.Question),
                    caseMode: TextNormalizingEstimator.CaseMode.Lower,
                    keepDiacritics: false,
                    keepPunctuations: false,
                    keepNumbers: true)

                .Append(_ml.Transforms.Text.FeaturizeText(
                    outputColumnName: "Features",
                    inputColumnName: "QuestionNorm"))

                .Append(_ml.Transforms.Conversion.MapValueToKey(
                    outputColumnName: "Label",
                    inputColumnName: nameof(JobSample.Category)))

                .Append(_ml.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                    featureColumnName: "Features",
                    labelColumnName: "Label",
                    maximumNumberOfIterations: 300))

                .Append(_ml.Transforms.Conversion.MapKeyToValue(
                    outputColumnName: "PredictedLabel",
                    inputColumnName: "PredictedLabel"));

            var model = pipeline.Fit(dataView);

            _categoryModel = model;
            _catEngine = _ml.Model.CreatePredictionEngine<JobSample, CategoryPrediction>(model);

            // Quick evaluation
            var predictions = model.Transform(dataView);
            var metrics = _ml.MulticlassClassification.Evaluate(predictions, labelColumnName: "Label");

            Console.WriteLine("Category model trained!");
            Console.WriteLine($"Micro Accuracy: {metrics.MicroAccuracy:P2}");
            Console.WriteLine($"Macro Accuracy: {metrics.MacroAccuracy:P2}");
            Console.WriteLine($"Categories: {metrics.PerClassLogLoss.Count}");
        }

        // ------------------------------------------------------------
        // 3. Train Per-Category Models (model_*.zip)
        // ------------------------------------------------------------
        public void TrainPerCategoryModels(List<JobSample> data)
        {
            if (data == null || !data.Any())
                throw new ArgumentException("No data to train per-category models.");

            var categories = data.Select(x => x.Category).Distinct().ToList();

            Parallel.ForEach(categories, category =>
            {
                var catData = data.Where(x => x.Category == category).ToList();
                if (!catData.Any()) return;

                var dv = _ml.Data.LoadFromEnumerable(catData);

                var pipeline = _ml.Transforms.Text
                    .NormalizeText(
                        outputColumnName: "QuestionNorm",
                        inputColumnName: nameof(JobSample.Question),
                        caseMode: TextNormalizingEstimator.CaseMode.Lower,
                        keepDiacritics: false,
                        keepPunctuations: false,
                        keepNumbers: true)

                    .Append(_ml.Transforms.Text.FeaturizeText(
                        outputColumnName: "Features",
                        inputColumnName: "QuestionNorm"))

                    .Append(_ml.Transforms.Conversion.MapValueToKey(
                        outputColumnName: "Label",
                        inputColumnName: nameof(JobSample.Answer)))

                    .Append(_ml.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                        featureColumnName: "Features",
                        labelColumnName: "Label",
                        maximumNumberOfIterations: 200))

                    .Append(_ml.Transforms.Conversion.MapKeyToValue(
                        outputColumnName: "PredictedLabel",
                        inputColumnName: "PredictedLabel"));

                var model = pipeline.Fit(dv);
                var normCat = Normalize(category);
                _perCategoryModels[normCat] = model;
                _perCategoryEngines[normCat] = _ml.Model.CreatePredictionEngine<JobSample, AnswerPrediction>(model);

                Console.WriteLine($"Trained per-category model for: {category} ({catData.Count} samples)");
            });
        }

        // ------------------------------------------------------------
        // 4. Save Models (category_classifier.zip + model_*.zip)
        // ------------------------------------------------------------
        public void SaveCategoryModel(string path)
        {
            if (_categoryModel == null)
                throw new InvalidOperationException("Category model not trained.");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _ml.Model.Save(_categoryModel, _ml.Data.LoadFromEnumerable(new[] { new JobSample() }).Schema, path);
            Console.WriteLine($"Category model saved: {path}");
        }

        public void SavePerCategoryModels(string folder)
        {
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            foreach (var kv in _perCategoryModels)
            {
                var path = Path.Combine(folder, $"model_{kv.Key}.zip");
                _ml.Model.Save(kv.Value, _ml.Data.LoadFromEnumerable(new[] { new JobSample() }).Schema, path);
                Console.WriteLine($"Per-category model saved: {path}");
            }
        }

        public void SaveCategoryAndPerCategoryModels(string folder)
        {
            SaveCategoryModel(Path.Combine(folder, "category_classifier.zip"));
            SavePerCategoryModels(folder);
            Console.WriteLine($"All models saved to: {folder}");
        }

        // ------------------------------------------------------------
        // 5. Load Models (Fast Startup)
        // ------------------------------------------------------------
        public void LoadCategoryModel(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Category model not found: {path}");

            var model = _ml.Model.Load(path, out var schema);
            _categoryModel = model;
            _catEngine = _ml.Model.CreatePredictionEngine<JobSample, CategoryPrediction>(model);
            Console.WriteLine($"Category model loaded: {path}");
        }

        public void LoadPerCategoryModels(string folder)
        {
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException($"Models folder not found: {folder}");

            var files = Directory.GetFiles(folder, "model_*.zip");
            foreach (var file in files)
            {
                var cat = Path.GetFileNameWithoutExtension(file)["model_".Length..];
                var model = _ml.Model.Load(file, out _);
                _perCategoryEngines[cat] = _ml.Model.CreatePredictionEngine<JobSample, AnswerPrediction>(model);
                Console.WriteLine($"Per-category model loaded: {file}");
            }
        }

        // ------------------------------------------------------------
        // 6. Prediction
        // ------------------------------------------------------------
        public string PredictJobCategory(string question)
        {
            if (_catEngine == null)
                throw new InvalidOperationException("Category model not loaded.");

            var sample = new JobSample { Question = question };
            CategoryPrediction prediction = _catEngine.Predict(sample);
            return prediction.Category ?? "unknown";
        }

        public string PredictAnswer(string question)
        {
            var category = PredictJobCategory(question);
            var normCat = Normalize(category);

            if (!_perCategoryEngines.TryGetValue(normCat, out var engine))
                return $"No model for category: {category}";

            var sample = new JobSample { Question = question, Category = category };
            AnswerPrediction prediction = engine.Predict(sample);
            return prediction.PredictedLabel ?? "no answer";
        }

        public List<MatchResult> PredictMatches(ResumeInput resume, int topN)
        {
            var resumeTokens = new HashSet<string>(
         System.Text.RegularExpressions.Regex.Matches(resume.FullText.ToLowerInvariant(), @"\w+")
             .Select(m => m.Value));

            var scoredJobs = new List<(JobSample job, int score)>();

            foreach (var job in _allJobSamples)
            {
                var jobTokens = new HashSet<string>(
                    System.Text.RegularExpressions.Regex.Matches(job.Question.ToLowerInvariant(), @"\w+")
                        .Select(m => m.Value));

                int commonWords = resumeTokens.Intersect(jobTokens).Count();

                if (commonWords > 0)
                    scoredJobs.Add((job, commonWords));
            }

            var topMatches = scoredJobs
                .OrderByDescending(x => x.score)
                .Take(topN)
                .Select(x => new MatchResult
                {
                    JobTitle = x.job.Answer,
                    Company = "نامشخص", // اگر داری از JSONL بخون
                    Category = x.job.Category,
                    MatchScore = (float)x.score,
                    HiringProbability = $"{(x.score * 10)}%"
                })
                .ToList();

            return topMatches;
        }

        private static string Normalize(string s) =>
            s.Replace(" ", "_").Replace("-", "_").Replace("/", "_").Trim().ToLowerInvariant();

        public void TrainAnswerModels(List<JobSample> data)
        {
            if (data == null || !data.Any())
                throw new ArgumentException("Data is empty.");

            var grouped = data
                .Where(x => !string.IsNullOrWhiteSpace(x.Question) && !string.IsNullOrWhiteSpace(x.Answer))
                .GroupBy(x => x.Category)
                .ToList();

            Console.WriteLine($"آموزش {grouped.Count} مدل اختصاصی (یک مدل برای هر دسته شغلی)...");

            Parallel.ForEach(grouped, group =>
            {
                var category = group.Key;
                var samples = group.ToList();

                if (samples.Count < 5)
                {
                    Console.WriteLine($"  هشدار: دسته '{category}' فقط {samples.Count} نمونه دارد → رد شد.");
                    return;
                }

                var dataView = _ml.Data.LoadFromEnumerable(samples);

                var pipeline = _ml.Transforms.Text
                    .NormalizeText(
                        outputColumnName: "QuestionNorm",
                        inputColumnName: nameof(JobSample.Question),
                        caseMode: TextNormalizingEstimator.CaseMode.Lower,
                        keepDiacritics: false,
                        keepPunctuations: false,
                        keepNumbers: true)

                    .Append(_ml.Transforms.Text.FeaturizeText(
                        outputColumnName: "Features",
                        inputColumnName: "QuestionNorm"))

                    .Append(_ml.Transforms.Conversion.MapValueToKey(
                        outputColumnName: "Label",
                        inputColumnName: nameof(JobSample.Answer))) // پیش‌بینی Answer بر اساس Question

                    .Append(_ml.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                        featureColumnName: "Features",
                        labelColumnName: "Label",
                        maximumNumberOfIterations: 200))

                    .Append(_ml.Transforms.Conversion.MapKeyToValue(
                        outputColumnName: "PredictedLabel",
                        inputColumnName: "PredictedLabel"));

                var model = pipeline.Fit(dataView);
                var engine = _ml.Model.CreatePredictionEngine<JobSample, AnswerPrediction>(model);

                var safeName = NormalizeCategoryName(category);
                _answerModels[safeName] = model;
                _answerEngines[safeName] = engine;

                Console.WriteLine($"  مدل '{category}' آموزش دید ({samples.Count} نمونه)");
            });

            Console.WriteLine($"آموزش {_answerModels.Count} مدل اختصاصی با موفقیت به پایان رسید.");
        }

        public void SaveAnswerModels(string folder)
        {
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            foreach (var kv in _answerModels)
            {
                var safeName = kv.Key;
                var path = Path.Combine(folder, $"model_{safeName}.zip");

                // باید Schema رو از یه نمونه خالی بگیریم
                var emptySample = new JobSample();
                var schema = _ml.Data.LoadFromEnumerable(new[] { emptySample }).Schema;

                _ml.Model.Save(kv.Value, schema, path);
                Console.WriteLine($"مدل ذخیره شد: model_{safeName}.zip");
            }
        }

        public void LoadAnswerModels(string folder)
        {
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException($"Models folder not found: {folder}");

            var files = Directory.GetFiles(folder, "model_*.zip");
            Console.WriteLine($"بارگذاری {files.Length} مدل اختصاصی...");

            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var safeName = fileName["model_".Length..]; // model_آرایشگر.zip → آرایشگر

                var model = _ml.Model.Load(file, out var schema);
                var engine = _ml.Model.CreatePredictionEngine<JobSample, AnswerPrediction>(model);

                _answerModels[safeName] = model;
                _answerEngines[safeName] = engine;

                Console.WriteLine($"مدل بارگذاری شد: {fileName}");
            }
        }

        public void SaveCategoryAndAnswerModels(string folder)
        {
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            // ذخیره مدل دسته‌بندی اصلی
            if (_categoryModel != null)
            {
                var catPath = Path.Combine(folder, "category_classifier.zip");
                var schema = _ml.Data.LoadFromEnumerable(new[] { new JobSample() }).Schema;
                _ml.Model.Save(_categoryModel, schema, catPath);
                Console.WriteLine($"مدل دسته‌بندی ذخیره شد: category_classifier.zip");
            }
            SaveAnswerModels(folder);
            Console.WriteLine("همه مدل‌ها با موفقیت ذخیره شدند!");
        }

        // ذخیره مدل‌های اختصاصی
        private static string NormalizeCategoryName(string category) =>
            category
                .Replace(" ", "_")
                .Replace("-", "_")
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace(":", "")
                .Replace("?", "")
                .Trim();
    }
}
