using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ML.Data;

namespace jobscrapper.Models
{
    public class CategoryPrediction
    {
        [ColumnName("PredictedLabel")]
        public string Category { get; set; } = "";

        public float[] Score { get; set; }
    }
}
