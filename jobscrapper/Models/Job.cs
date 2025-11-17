using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace jobscrapper.Models
{
    public class Job
    {
        public string Title { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Salary { get; set; } = string.Empty;
        public string PostedDate { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string FullDescription { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
        public string JobText { get; set; } = string.Empty; // Concat for ML
        public string FullText => $"{Title} {ShortDescription}";
    }

}
