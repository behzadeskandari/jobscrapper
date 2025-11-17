using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace jobscrapper.Utils
{
    public class TfidfVectorizer
    {
        private readonly Dictionary<string, int> _vocab = new();
        private  float[] _idf;
        private  string[] _terms;

        public void Fit(string[] documents)
        {
            var termDocCount = new Dictionary<string, int>();
            var allTokens = new List<List<string>>();

            foreach (var doc in documents)
            {
                var tokens = Tokenize(doc);
                var unique = new HashSet<string>(tokens);
                foreach (var t in unique)
                    termDocCount[t] = termDocCount.GetValueOrDefault(t) + 1;
                allTokens.Add(tokens);
            }

            // Build vocab
            int idx = 0;
            foreach (var t in termDocCount.Keys.OrderBy(t => t))
                _vocab[t] = idx++;

            _terms = _vocab.Keys.ToArray();
            _idf = new float[_vocab.Count];
            int N = documents.Length;
            for (int i = 0; i < _terms.Length; i++)
                _idf[i] = MathF.Log(N / (1f + termDocCount[_terms[i]]));
        }

        public float[] Transform(string document)
        {
            var tokens = Tokenize(document);
            var tf = new float[_vocab.Count];
            int tokenCount = tokens.Count;

            foreach (var t in tokens)
                if (_vocab.TryGetValue(t, out int idx))
                    tf[idx]++;

            var vec = new float[_vocab.Count];
            for (int i = 0; i < tf.Length; i++)
                vec[i] = tf[i] / (tokenCount + 1f) * _idf[i]; // smoothed TF*IDF

            return vec;
        }

        private static List<string> Tokenize(string text)
        {
            return Regex
                .Matches(text.ToLowerInvariant(), @"\w+")
                .Cast<Match>()
                .Select(m => m.Value)
                .Where(w => w.Length > 2)
                .ToList();
        }
    }
}
