using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace jobscrapper.Models
{
    public class MatchResult
    {
        public string JobTitle { get; set; } = "";
        public string Company { get;  set; } = "";
        public string Category { get; set; } = "";
        public float MatchScore { get; set; }          // 0.0 to 1.0
        public string HiringProbability { get; set; } = "";  // e.g., "92.5%"

        // Optional: for pretty printing
        public override string ToString()
        {
            return $"{JobTitle} @ {Company} [{Category}] → Match: {MatchScore:P2} → Hiring: {HiringProbability}";
        }
    }
}
