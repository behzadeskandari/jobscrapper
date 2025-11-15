using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace jobscrapper
{
    public class Resume
    {
        public string FullText { get; set; } = "";
        public string Name { get; set; } = "";
        public string Education { get; set; } = "";
        public int YearsExperience { get; set; }
        public string City { get; set; } = "";
    }
}
