using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using jobscrapper.Models;
using Microsoft.ML;

namespace jobscrapper.Services
{
    public class JobPredictor
    {
        private readonly MLContext _mlContext;
        private readonly Dictionary<string, ITransformer> _categoryModels = new();
        private readonly BertFeaturizer _bert;
        private readonly List<Job> _allJobs;

        public JobPredictor(string modelFolder = "Models")
        {
            _mlContext = new MLContext(seed: 42);
            _bert = new BertFeaturizer(_mlContext);
            _allJobs = DataPipeline.LoadAllJobs();

            // Load all category models
            foreach (var file in Directory.GetFiles(modelFolder, "*_model.zip"))
            {
                var category = Path.GetFileNameWithoutExtension(file).Replace("_model", "");
                _categoryModels[category] = _mlContext.Model.Load(file, out _);
            }
        }

        public List<MatchResult> Predict(ResumeInput resume, int topN = 5)
        {
            // Step 1: Predict resume category using a global classifier (or first model)
            var resumeEmbedding = _bert.GetEmbedding(resume.FullText);
            var predictedCategory = PredictCategory(resumeEmbedding);  // Implement simple category predictor

            // Step 2: Load category-specific jobs & model
            var categoryJobs = _allJobs.Where(j => j.Category == predictedCategory).ToList();
            var categoryModel = _categoryModels.GetValueOrDefault(predictedCategory);

            // Step 3: Compute similarities (BERT-based)
            var jobEmbeddings = categoryJobs.ToDictionary(j => j.Link ?? "", j => _bert.GetEmbedding(j.FullText));
            var similarities = jobEmbeddings
                .Select(kv => new { Job = categoryJobs.First(j => (j.Link ?? "") == kv.Key), Sim = CosineSimilarity(resumeEmbedding, kv.Value) })
                .OrderByDescending(x => x.Sim)
                .Take(topN)
                .ToList();

            // Step 4: Use category model for sub-predictions (e.g., refine score)
            var results = new List<MatchResult>();
            var predEngine = _mlContext.Model.CreatePredictionEngine<JobInput, JobMatchPrediction>(categoryModel);
            foreach (var s in similarities)
            {
                var input = new JobInput { Text = s.Job.FullText };
                var subPred = predEngine.Predict(input);
                var prob = SigmoidProbability(s.Sim);

                results.Add(new MatchResult
                {
                    JobTitle = s.Job.Title,
                    Company = s.Job.Company,
                    Category = predictedCategory,
                    //SubCategory = subPred.Category,
                    MatchScore = s.Sim,
                    HiringProbability = $"{prob:F1}%"
                });
            }
            return results;
        }

        private string PredictCategory(float[] embedding)
        {
            // Simple: Pick most common or use a global model; for now, heuristic from skills
            var text = "";  // Reverse-engineer from embedding if needed
            return text.Contains("develop") ? "IT" : "Marketing";  // Placeholder
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

        private static float SigmoidProbability(float sim) => 100f / (1f + MathF.Exp(-12f * (sim - 0.5f)));
    }
}
