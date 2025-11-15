using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace jobscrapper
{
    public class BertFeaturizer
    {
        //private readonly MLContext _ml;
        //private readonly ITransformer _model;

        //public BertFeaturizer(MLContext ml, string modelFolder = "Models/bert-fa-base-uncased")
        //{
        //    _ml = ml;

        //    var pipeline = ml.Transforms
        //        .ApplyOnnxModel(
        //            modelFile: Path.Combine(modelFolder, "model.onnx"),
        //            outputColumnNames: new[] { "pooled_output" },
        //            inputColumnNames: new[] { "input_ids", "attention_mask" })
        //        .Append(ml.Transforms.CopyColumns("Features", "pooled_output"));

        //    // Dummy data to fit the pipeline (ONNX needs schema)
        //    var dummy = ml.Data.LoadFromEnumerable(new[] { new { Text = "" } });
        //    _model = pipeline.Fit(dummy);
        //}

        //public float[] GetEmbedding(string text)
        //{
        //    var tokenizer = new BertTokenizer(
        //        vocabFile: "Models/bert-fa-base-uncased/vocab.txt",
        //        doLowerCase: true);

        //    var encoded = tokenizer.Encode(text, maxLength: 512);
        //    var data = new[]
        //    {
        //    new { input_ids = encoded.Ids, attention_mask = encoded.AttentionMask }
        //};
        //    var idv = _ml.Data.LoadFromEnumerable(data);
        //    var transformed = _model.Transform(idv);
        //    var features = transformed.GetColumn<float[]>("Features").First();
        //    return features;
        //}
    }
}
