using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using jobscrapper.Models;

namespace jobscrapper.Interfaces
{
    public interface IJobDataLoader
    {
        List<Job> LoadAllJobs(string folder = "Data");
        List<Job> LoadJobsByCategory(string category, string folder = "Data");
    }
}
