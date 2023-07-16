using System;

namespace SysBot.ACNHOrders
{
    public static class LevenshteinDistance
    {
        /// <summary>
        /// Calculer la distance entre deux chaînes de caractères.
        /// http://www.dotnetperls.com/levenshtein
        /// https://stackoverflow.com/a/13793600
        /// </summary>
        public static int Compute(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            // Étape 1
            if (n == 0)
            {
                return m;
            }

            if (m == 0)
            {
                return n;
            }

            // Étape 2
            for (int i = 0; i <= n; d[i, 0] = i++)
            {
            }

            for (int j = 0; j <= m; d[0, j] = j++)
            {
            }

            // Étape 3
            for (int i = 1; i <= n; i++)
            {
                //Étape 4
                for (int j = 1; j <= m; j++)
                {
                    // Étape 5
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;

                    // Étape 6
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            // Étape 7
            return d[n, m];
        }
    }
}
