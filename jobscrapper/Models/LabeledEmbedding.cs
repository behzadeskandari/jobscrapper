using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ML.Data;

namespace jobscrapper.Models
{
    internal class LabeledEmbedding
    {
        [VectorType(768)]
        public float[] Features { get; set; } = new float[768];

        public string Label { get; set; } = "";
    }
}
