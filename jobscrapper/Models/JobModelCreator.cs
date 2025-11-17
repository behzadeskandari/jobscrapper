using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ML;

namespace jobscrapper.Models
{
    public static class JobModelCreator
    {
        private static readonly MLContext _mlContext = new(seed: 42);
        private static readonly BertFeaturizer _bert = new BertFeaturizer(_mlContext);

        public static void CreateModels(string dataFolder = "Data", string modelFolder = "Models")
        {
            if (!Directory.Exists(modelFolder)) Directory.CreateDirectory(modelFolder);

            var categories = Directory.GetDirectories(dataFolder).Select(Path.GetFileName).ToArray();
            foreach (var category in categories)
            {
                Console.WriteLine($"Training model for category: {category}");

                // 1. Load jobs for this category
                var jobs = DataPipeline.LoadJobsByCategory(category, dataFolder);
                if (jobs.Count < 10)
                {
                    Console.WriteLine($"  Skipping {category}: only {jobs.Count} jobs (need ≥10)");
                    continue;
                }

                // 2. Build training rows with sub-category labels
                var trainingRows = jobs.Select(j => new JobInput
                {
                    Text = j.FullText,
                    Label = DeriveSubCategory(j.Title, j.ShortDescription) // Fixed: Description
                }).ToList();

                // 3. Pre-compute BERT embeddings (float[768])
                Console.WriteLine($"  Computing BERT embeddings for {trainingRows.Count} jobs...");
                var embeddings = new float[trainingRows.Count][];
                for (int i = 0; i < trainingRows.Count; i++)
                {
                    embeddings[i] = _bert.GetEmbedding(trainingRows[i].Text);
                }

                // 4. Create ML.NET data with Features + Label
                var labeledData = trainingRows
                    .Select((row, idx) => new LabeledEmbedding
                    {
                        Label = row.Label,
                        Features = embeddings[idx]
                    })
                    .ToList();

                var dataView = _mlContext.Data.LoadFromEnumerable(labeledData);

                // 5. Build training pipeline
                var pipeline = _mlContext.Transforms.Conversion.MapValueToKey("LabelKey", "Label")
                    .Append(_mlContext.MulticlassClassification.Trainers
                        .SdcaMaximumEntropy("LabelKey", "Features", maximumNumberOfIterations: 100))
                    .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel", "LabelKey"));

                // 6. Train & save
                var model = pipeline.Fit(dataView);
                var modelPath = Path.Combine(modelFolder, $"{category}_model.zip");
                Directory.CreateDirectory(modelFolder);
                _mlContext.Model.Save(model, dataView.Schema, modelPath);

                Console.WriteLine($"  Model saved: {modelPath}");
            }
        }

        private static string DeriveSubCategory(string title, string desc)
        {
            var lower = (title + " " + desc).ToLower();
            return lower.Contains("developer") ? "Developer" :
                   lower.Contains("scientist") ? "Data Scientist" :
                   lower.Contains("engineer") ? "Engineer" : "Other";
        }
    }
}
