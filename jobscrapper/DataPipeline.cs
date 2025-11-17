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
        public static List<Job> LoadJobs(string folder = "Data")
        {
            var jobs = new List<Job>();
            foreach (var file in Directory.GetFiles(folder, "jobs_*.jsonl"))
            {
                var lines = File.ReadAllLines(file, Encoding.UTF8);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var job = JsonSerializer.Deserialize<Job>(line)!;
                    jobs.Add(job);
                }
            }
            return jobs;
        }
    }
}
