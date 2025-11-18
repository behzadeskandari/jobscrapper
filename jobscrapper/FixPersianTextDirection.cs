using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace jobscrapper
{
    public static class FixPersianTextDirections
    {
        public static string FixPersianTextDirection(string input)
        {
            var lines = input.Split('\n');
            var fixedLines = new List<string>();

            foreach (var line in lines)
            {
                // Simple heuristic: if line contains mostly Persian letters, reverse it
                int persianCount = line.Count(c => (c >= 0x0600 && c <= 0x06FF) || (c >= 0xFB50 && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF));
                if (persianCount > line.Length / 2)
                {
                    // Reverse characters or words (depending on your needs)
                    var reversed = new string(line.Reverse().ToArray());
                    fixedLines.Add(reversed);
                }
                else
                {
                    fixedLines.Add(line);
                }
            }

            return string.Join('\n', fixedLines);
        }
    }
}
