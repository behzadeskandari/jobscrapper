using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using jobscrapper.Models;
using jobscrapper.Utils;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.Text;

namespace jobscrapper.Services
{
    public class JobMatcher
    {
        private readonly MLContext _mlContext;
        private readonly ITransformer _categoryModel;
        private readonly TfidfVectorizer _tfidf;
        private readonly List<Job> _jobs;
        private readonly Dictionary<string, float[]> _jobVectors;

        public JobMatcher(List<Job> jobs, string modelPath = "category_model.zip")
        {
            _mlContext = new MLContext(seed: 42);
            _jobs = jobs;

            // Load or train category classifier
            if (File.Exists(modelPath))
            {
                _categoryModel = _mlContext.Model.Load(modelPath, out _);
            }
            else
            {
                _categoryModel = TrainCategoryClassifier(jobs);
                _mlContext.Model.Save(_categoryModel, GetSchema(), modelPath);
            }

            // Build TF-IDF (custom, as ML.NET's FeaturizeText is for classification features)
            var allTexts = jobs.Select(j => j.JobText).ToArray();
            _tfidf = new TfidfVectorizer();
            _tfidf.Fit(allTexts);

            // Pre-compute job vectors
            _jobVectors = jobs.Select((j, i) => new { j, vec = _tfidf.Transform(j.JobText) })
                              .ToDictionary(x => $"{x.j.Title}|{x.j.Company}", x => x.vec);
        }

        private ITransformer TrainCategoryClassifier(List<Job> jobs)
        {
            var data = _mlContext.Data.LoadFromEnumerable(
                jobs.Select(j => new JobInput { Text = j.JobText, Label = j.Category }));

            var pipeline = _mlContext.Transforms.Text.FeaturizeText("Features", nameof(JobInput.Text))
                .Append(_mlContext.MulticlassClassification.Trainers
                .SdcaMaximumEntropy("Label", "Features", l2Regularization : 0.1f,l1Regularization: 0f,  maximumNumberOfIterations: 100))
                .Append(_mlContext.Transforms.Conversion.MapValueToKey("Label", "Label"))
                .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel", "Label"));

            return pipeline.Fit(data);
        }

        private DataViewSchema GetSchema() => _mlContext.Data.LoadFromEnumerable(new[] { new JobInput() }).Schema;

        public List<MatchResult> Predict(ResumeInput resume, int topN = 5)
        {
            var resumeVec = _tfidf.Transform(resume.FullText);

            var similarities = _jobVectors
                .Select(kv => new
                {
                    Job = kv.Key.Split('|')[0],
                    Company = kv.Key.Split('|')[1],
                    Sim = CosineSimilarity(resumeVec, kv.Value)
                })
                .OrderByDescending(x => x.Sim)
                .Take(topN)
                .ToList();

            var predEngine = _mlContext.Model.CreatePredictionEngine<JobInput, JobMatchPrediction>(_categoryModel);

            var results = new List<MatchResult>();
            foreach (var s in similarities)
            {
                var job = _jobs.First(j => j.Title == s.Job && j.Company == s.Company);
                var input = new JobInput { Text = job.JobText };
                var prediction = predEngine.Predict(input);
                var prob = SigmoidProbability(s.Sim);

                results.Add(new MatchResult
                {
                    JobTitle = job.Title,
                    Company = job.Company,
                    Category = prediction.Category ?? job.Category,  // Fallback to stored
                    MatchScore = s.Sim,
                    HiringProbability = $"{prob:F1}%"
                });
            }
            return results;
        }

        private static float CosineSimilarity(float[] a, float[] b)
        {
            float dot = 0, normA = 0, normB = 0;
            for (int i = 0; i < a.Length && i < b.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }
            return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB) + 1e-9f);
        }

        private static float SigmoidProbability(float similarity)
        {
            return 100f / (1f + MathF.Exp(-12f * (similarity - 0.5f)));
        }
    }
}
