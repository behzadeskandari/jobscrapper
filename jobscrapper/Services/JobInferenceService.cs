using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using jobscrapper.Interfaces;
using jobscrapper.Models;
using Microsoft.ML;

namespace jobscrapper.Services
{
    public class JobInferenceService : IJobInferenceService
    {
        private readonly MLContext _ml = new();
        private ITransformer? _categoryModel;
        private PredictionEngine<JobSample, CategoryPrediction>? _catEngine;

        private readonly ConcurrentDictionary<string, ITransformer> _answerModels = new();
        private readonly ConcurrentDictionary<string, PredictionEngine<JobSample, AnswerPrediction>> _answerEngines = new();

        // ------------------------------------------------------------
        // 1. Load JSONL
        // ------------------------------------------------------------
        public List<JobSample> LoadJsonl(string path)
        {
            return File.ReadLines(path)
                .Select(line => System.Text.Json.JsonSerializer.Deserialize<JobSample>(line))
                .Where(x => x != null)
                .ToList()!;
        }

        // ------------------------------------------------------------
        // 2. Train Category Model
        // ------------------------------------------------------------
        public void TrainCategoryModel(List<JobSample> data)
        {
            var dv = _ml.Data.LoadFromEnumerable(data);

            var pipeline = _ml.Transforms.Text
                   .FeaturizeText("Features", nameof(JobSample.Question))
                   .Append(_ml.Transforms.Conversion.MapValueToKey("Label", nameof(JobSample.Category)))
                   .Append(_ml.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
                   .Append(_ml.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            _categoryModel = pipeline.Fit(dv);

            _catEngine = _ml.Model.CreatePredictionEngine<JobSample, CategoryPrediction>(_categoryModel);
        }

        // ------------------------------------------------------------
        // 3. Train Answer Models (One per Category)
        // ------------------------------------------------------------
        public void TrainAnswerModels(List<JobSample> data)
        {
            var cats = data.Select(x => x.Category).Distinct();

            Parallel.ForEach(cats, cat =>
            {
                var filtered = data.Where(x => x.Category == cat).ToList();
                if (!filtered.Any()) return;

                var dv = _ml.Data.LoadFromEnumerable(filtered);

                var pipeline = _ml.Transforms.Text.FeaturizeText("Features", nameof(JobSample.Question))
                    .Append(_ml.Transforms.Conversion.MapValueToKey("Label", nameof(JobSample.Answer)))
                    .Append(_ml.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
                    .Append(_ml.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

                var model = pipeline.Fit(dv);

                _answerModels[Normalize(cat)] = model;
                _answerEngines[Normalize(cat)] =
                    _ml.Model.CreatePredictionEngine<JobSample, AnswerPrediction>(model);
            });
        }

        // ------------------------------------------------------------
        // 4. Save Models
        // ------------------------------------------------------------
        public void SaveCategoryModel(string path)
        {
            var dummy = _ml.Data.LoadFromEnumerable(new[] { new JobSample() });
            _ml.Model.Save(_categoryModel!, dummy.Schema, path);
        }

        public void SaveCategoryAndAnswerModels(string folder)
        {
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            SaveCategoryModel(Path.Combine(folder, "category.zip"));

            foreach (var kv in _answerModels)
            {
                var dummy = _ml.Data.LoadFromEnumerable(new[] { new JobSample() });
                _ml.Model.Save(kv.Value, dummy.Schema, Path.Combine(folder, $"answer_{kv.Key}.zip"));
            }
        }

        // ------------------------------------------------------------
        // 5. Load Models
        // ------------------------------------------------------------
        public void LoadCategoryModel(string path)
        {
            var model = _ml.Model.Load(path, out _);
            _catEngine = _ml.Model.CreatePredictionEngine<JobSample, CategoryPrediction>(model);
        }

        public void LoadAnswerModels(string folder)
        {
            var files = Directory.GetFiles(folder, "answer_*.zip");

            foreach (var file in files)
            {
                var cat = Path.GetFileNameWithoutExtension(file).Replace("answer_", "");
                var model = _ml.Model.Load(file, out _);
                var engine = _ml.Model.CreatePredictionEngine<JobSample, AnswerPrediction>(model);

                _answerEngines[cat] = engine;
            }
        }

        // ------------------------------------------------------------
        // 6. Prediction
        // ------------------------------------------------------------
        public string PredictJobCategory(string question)
        {
            var pred = _catEngine?.Predict(new JobSample { Question = question });
            return pred?.PredictedLabel ?? "unknown";
        }

        public string PredictAnswer(string question)
        {
            var cat = PredictJobCategory(question);
            var normCat = Normalize(cat);

            if (!_answerEngines.TryGetValue(normCat, out var engine))
                return $"No answer model for category: {cat}";

            var pred = engine.Predict(new JobSample { Question = question });

            return pred?.PredictedLabel ?? "no answer found";
        }

        // ------------------------------------------------------------
        private static string Normalize(string s) =>
            s.Replace(" ", "_")
             .Replace("-", "_")
             .Trim()
             .ToLowerInvariant();
    }
}
