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
        /// <summary>
        /// Lightweight BERT tokenizer using a pre-loaded vocab file.
        /// Supports [CLS], [SEP], [PAD], [UNK], [MASK].
        /// Returns int64 arrays required by ONNX BERT models.
        /// </summary>
      
            private readonly List<string> _vocab; // Ordered list for fast IndexOf
            private readonly Dictionary<string, long> _tokenToId; // Fast lookup

            public SimpleBertTokenizer(string vocabPath)
            {
                if (!File.Exists(vocabPath))
                    throw new FileNotFoundException($"Vocab file not found: {vocabPath}");

                // Load vocab in order
                _vocab = File.ReadAllLines(vocabPath).Select(line => line.Trim()).ToList();

                // Ensure special tokens exist
                EnsureSpecialToken("[PAD]", 0);
                EnsureSpecialToken("[UNK]", 100);
                EnsureSpecialToken("[CLS]", 101);
                EnsureSpecialToken("[SEP]", 102);
                EnsureSpecialToken("[MASK]", 103);

                // Build reverse lookup
                _tokenToId = _vocab.Select((token, index) => new { token, index })
                                  .ToDictionary(x => x.token, x => (long)x.index);
            }

            private void EnsureSpecialToken(string token, int expectedId)
            {
                if (!_vocab.Contains(token))
                {
                    if (_vocab.Count <= expectedId)
                        _vocab.Insert(expectedId, token);
                    else
                        _vocab[expectedId] = token;
                }
            }

            /// <summary>
            /// Encodes text into BERT input IDs and attention mask.
            /// </summary>
            /// <param name="text">Input text</param>
            /// <param name="maxLength">Max sequence length (e.g., 128)</param>
            /// <returns>(inputIds: long[], attentionMask: long[])</returns>
            public (long[] Ids, long[] AttentionMask) Encode(string text, int maxLength = 128)
            {
                if (string.IsNullOrWhiteSpace(text))
                    text = "";

                // Step 1: Basic word splitting (lower-case)
                var words = text.ToLowerInvariant()
                                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                .Where(w => w.Length > 0)
                                .ToList();

                // Step 2: Build token list: [CLS] + words + [SEP]
                var tokens = new List<string> { "[CLS]" };
                tokens.AddRange(words);
                tokens.Add("[SEP]");

                // Step 3: Convert to IDs (use [UNK] if missing)
                var idsList = new List<long>();
                foreach (var token in tokens)
                {
                    idsList.Add(_tokenToId.TryGetValue(token, out var id) ? id : _tokenToId["[UNK]"]);
                }

                // Step 4: Truncate if too long
                if (idsList.Count > maxLength)
                {
                    idsList = idsList.Take(maxLength).ToList();
                }

                // Step 5: Pad to maxLength
                int currentLength = idsList.Count;
                int padSize = maxLength - currentLength;

                var inputIds = new long[maxLength];
                var attentionMask = new long[maxLength];

                // Copy real tokens
                for (int i = 0; i < currentLength; i++)
                {
                    inputIds[i] = idsList[i];
                    attentionMask[i] = 1;
                }

                // Pad with 0s
                for (int i = currentLength; i < maxLength; i++)
                {
                    inputIds[i] = 0; // [PAD]
                    attentionMask[i] = 0;
                }

                return (inputIds, attentionMask);
            }
    }
}
