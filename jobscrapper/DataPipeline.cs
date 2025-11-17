using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using jobscrapper.Models;

namespace jobscrapper
{
    public class DataPipeline
    {
        public static List<Job> LoadAllJobs(string folder = "Data")
        {
            var allJobs = new List<Job>();
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            foreach (var cat in Directory.GetDirectories(folder))
            {
                var category = Path.GetFileName(cat);
                var jsonlFile = Path.Combine(cat, $"jobs_{category}.jsonl");
                if (File.Exists(jsonlFile))
                {
                    var lines = File.ReadAllLines(jsonlFile, Encoding.UTF8);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var job = JsonSerializer.Deserialize<Job>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (job != null)
                        {
                            job.Category = category;  // Ensure category
                            allJobs.Add(job);
                        }
                    }
                }
            }
            Console.WriteLine($"Loaded {allJobs.Count} jobs across {Directory.GetDirectories(folder).Length} categories.");
            return allJobs;
        }

        // Category-specific loader (like medical sub-datasets)
        public static List<Job> LoadJobsByCategory(string category, string folder = "Data")
        {
            var jsonlFile = Path.Combine(folder, category, $"jobs_{category}.jsonl");
            if (!File.Exists(jsonlFile)) return new List<Job>();

            var lines = File.ReadAllLines(jsonlFile, Encoding.UTF8);
            var jobs = lines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonSerializer.Deserialize<Job>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }))
                .Where(job => job != null)
                .Cast<Job>()
                .ToList();
            return jobs;
        }
    }
}
