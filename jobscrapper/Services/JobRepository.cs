using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsvHelper;
using jobscrapper.Models;

namespace jobscrapper.Services
{
    public static class JobRepository
    {
        public static List<Job> Load(string csvPath)
        {
            using var reader = new StreamReader(csvPath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            return csv.GetRecords<Job>().ToList();
        }
    }
}
