using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ML.Data;

namespace jobscrapper.Models
{
    public class AnswerPrediction
    {
        [ColumnName("PredictedLabel")]
        public string PredictedLabel { get; set; }
    }
}
