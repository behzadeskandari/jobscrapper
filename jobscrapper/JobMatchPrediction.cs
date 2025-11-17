using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ML.Data;

namespace jobscrapper
{
    public class JobMatchPrediction
    {
        [ColumnName("PredictedLabel")]
        public string Category { get; set; } = "";

        public float[] Score { get; set; } = Array.Empty<float>();
    }
}
