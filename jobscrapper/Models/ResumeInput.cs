using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace jobscrapper.Models
{
    public class ResumeInput
    {
        public string FullText { get; set; } = "";
        public string Name { get; set; } = "";
        public string Education { get; set; } = "";
        public int YearsExperience { get; set; }
        public string City { get; set; } = "";
        public List<string> Skills { get; set; } = new();
    }
}
