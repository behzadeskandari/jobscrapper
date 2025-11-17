using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace jobscrapper
{
    public class BertFeaturizer
    {
        private readonly MLContext _ml;
        private readonly ITransformer _model;
        private readonly SimpleBertTokenizer _tokenizer;  // See below for implementation

        public BertFeaturizer(MLContext ml, string modelFolder = "Models/bert-base-uncased")
        {
            _ml = ml;
            _tokenizer = new SimpleBertTokenizer(Path.Combine(modelFolder, "vocab.txt"));

            var pipeline = _ml.Transforms.ApplyOnnxModel(
                modelFile: Path.Combine(modelFolder, "model.onnx"),
                outputColumnNames: new[] { "pooled_output" },  // BERT's [CLS] embedding (768-dim)
                inputColumnNames: new[] { "input_ids", "attention_mask" })
                .Append(_ml.Transforms.CopyColumns("Features", "pooled_output"));

            // Dummy data to fit (ONNX requires schema)
            var dummy = _ml.Data.LoadFromEnumerable(new[] { new BertInput { InputIds = new long[1], AttentionMask = new long[1] } });
            _model = pipeline.Fit(dummy);
        }

        public float[] GetEmbedding(string text)
        {
            var encoded = _tokenizer.Encode(text, maxLength: 512);
            var data = new[]
            {
                new BertInput
                {
                    InputIds = encoded.Ids,
                    AttentionMask = encoded.AttentionMask
                }
            };
            var idv = _ml.Data.LoadFromEnumerable(data);
            var transformed = _model.Transform(idv);
            var features = transformed.GetColumn<float[]>("Features").First();
            return features ?? new float[768];  // BERT-base dim
        }
    }

    // Input schema for ONNX
    public class BertInput
    {
        public long[] InputIds { get; set; } = Array.Empty<long>();
        public long[] AttentionMask { get; set; } = Array.Empty<long>();
    }

    // Simple tokenizer (expand with vocab.txt for WordPiece)
    public class SimpleBertTokenizer
    {
        private readonly HashSet<string> _vocab = new();
        public SimpleBertTokenizer(string vocabPath)
        {
            if (File.Exists(vocabPath))
                _vocab = new HashSet<string>(File.ReadAllLines(vocabPath));
            // Add common BERT tokens: [PAD], [UNK], [CLS], [SEP], [MASK]
            _vocab.Add("[PAD]"); _vocab.Add("[UNK]"); _vocab.Add("[CLS]"); _vocab.Add("[SEP]"); _vocab.Add("[MASK]");
        }

        public (long[] Ids, long[] AttentionMask) Encode(string text, int maxLength)
        {
            // Basic tokenization (improve with WordPiece for accuracy)
            var tokens = new List<string> { "[CLS]" };
            tokens.AddRange(text.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1));
            tokens.Add("[SEP]");

            var ids = tokens.Select(t => _vocab.Contains(t) ? Array.IndexOf(_vocab.ToArray(), t) : 100 /* [UNK] id */).ToArray();
            var mask = Enumerable.Repeat(1L, ids.Length).ToArray();

            // Pad/truncate
            if (ids.Length > maxLength) { ids = ids.Take(maxLength).ToArray(); mask = mask.Take(maxLength).ToArray(); }
            else
            {
                var padSize = maxLength - ids.Length;
                Array.Resize(ref ids, maxLength); Array.Fill(ids, 0L, ids.Length - padSize, padSize);  // Pad with 0
                Array.Resize(ref mask, maxLength); Array.Fill(mask, 0L, mask.Length - padSize, padSize);
            }

            return (ids.Select(long.Parse).ToArray(), mask);  // Cast to long[]
        }
    }
}
