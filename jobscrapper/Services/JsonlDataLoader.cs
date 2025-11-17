using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using jobscrapper.Interfaces;
using jobscrapper.Models;

namespace jobscrapper.Services
{
    public class JsonlDataLoader : IJobDataLoader
    {
        public List<Job> LoadAllJobs(string folder = "Data")
        {
            var jobs = new List<Job>();
            if (!Directory.Exists(folder)) return jobs;

            foreach (var catDir in Directory.GetDirectories(folder))
            {
                var category = Path.GetFileName(catDir);
                var file = Path.Combine(catDir, $"jobs_{category}.jsonl");
                if (File.Exists(file))
                {
                    var lines = File.ReadAllLines(file);
                    foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
                    {
                        var job = JsonSerializer.Deserialize<Job>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (job != null) { job.Category = category; jobs.Add(job); }
                    }
                }
            }
            return jobs;
        }

        public List<Job> LoadJobsByCategory(string category, string folder = "Data")
        {
            var file = Path.Combine(folder, category, $"jobs_{category}.jsonl");
            if (!File.Exists(file)) return new List<Job>();

            var lines = File.ReadAllLines(file);
            return lines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(line => JsonSerializer.Deserialize<Job>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }))
                .Where(j => j != null)
                .Cast<Job>()
                .ToList();
        }
    }
}
