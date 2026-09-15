namespace CodeSnippet.Services;

/// <summary>
/// VS Code "Quick Open"-style fuzzy matcher: <paramref name="query"/> chars must appear in
/// <paramref name="text"/> in order but not necessarily contiguously (e.g. "spv" matches
/// "SearchPopupViewModel"). Scored via DP so the optimal (highest-scoring) alignment is chosen
/// among all valid subsequence alignments, favoring consecutive runs, word/camelCase boundaries,
/// and matches near the start of the string — the same signals VS Code's picker ranks on.
/// </summary>
public static class FuzzyMatcher
{
    private const int BaseMatchScore = 1;
    private const int ConsecutiveBonus = 15;
    private const int StartOfStringBonus = 20;
    private const int BoundaryBonus = 12;
    private const int GapPenalty = 3;
    private const int NegInf = int.MinValue / 4;

    /// <summary>
    /// Reusable DP buffers for <see cref="TryMatch"/>, grown on demand and never shrunk, so repeated
    /// calls (e.g. once per prompt on every keystroke) don't allocate two 2D arrays each time.
    /// Not thread-safe — one instance per caller that only ever matches on one thread at a time.
    /// </summary>
    public sealed class Scratch
    {
        internal int[,] Dp = new int[0, 0];
        internal int[,] Best = new int[0, 0];
        internal int[] Positions = Array.Empty<int>();

        internal void EnsureCapacity(int n, int m)
        {
            if (Dp.GetLength(0) < n + 1 || Dp.GetLength(1) < m + 1)
            {
                Dp = new int[n + 1, m + 1];
                Best = new int[n + 1, m + 1];
            }

            if (Positions.Length < n)
            {
                Positions = new int[n];
            }
        }
    }

    /// <summary>
    /// Attempts to match <paramref name="query"/> as a fuzzy subsequence of <paramref name="text"/>.
    /// On success, <paramref name="score"/> is higher for better matches and <paramref name="ranges"/>
    /// (cleared first) is filled with the matched, non-overlapping (Start, Length) spans in
    /// ascending order, suitable for highlighting. <paramref name="scratch"/> supplies the DP buffers;
    /// reuse the same instance across calls to avoid allocating per match.
    /// </summary>
    public static bool TryMatch(string text, string query, out int score, List<(int Start, int Length)> ranges, Scratch scratch)
    {
        ranges.Clear();
        score = 0;

        if (string.IsNullOrEmpty(query))
        {
            return true;
        }

        if (string.IsNullOrEmpty(text) || query.Length > text.Length)
        {
            return false;
        }

        var n = query.Length;
        var m = text.Length;

        // dp[i, j]: best score matching query[0..i) with query[i-1] matched exactly at text[j-1].
        // best[i, j]: best score matching query[0..i) somewhere within text[0..j) (running max over dp[i, 1..j]).
        scratch.EnsureCapacity(n, m);
        var dp = scratch.Dp;
        var best = scratch.Best;

        for (var j = 0; j <= m; j++)
        {
            best[0, j] = 0;
        }

        for (var i = 1; i <= n; i++)
        {
            best[i, 0] = NegInf;
            dp[i, 0] = NegInf; // column 0 means "matched within zero text chars" — never valid for i >= 1;
                               // left at the array default of 0 it reads as a phantom match before the string.
            var queryChar = char.ToLowerInvariant(query[i - 1]);

            for (var j = 1; j <= m; j++)
            {
                dp[i, j] = NegInf;

                if (char.ToLowerInvariant(text[j - 1]) == queryChar)
                {
                    var charScore = BaseMatchScore + BoundaryScore(text, j - 1);

                    if (i == 1)
                    {
                        dp[i, j] = charScore;
                    }
                    else
                    {
                        var consecutive = dp[i - 1, j - 1] <= NegInf ? NegInf : dp[i - 1, j - 1] + ConsecutiveBonus;
                        var gapped = j - 2 < 0 || best[i - 1, j - 2] <= NegInf ? NegInf : best[i - 1, j - 2] - GapPenalty;
                        var better = Math.Max(consecutive, gapped);
                        dp[i, j] = better <= NegInf ? NegInf : charScore + better;
                    }
                }

                best[i, j] = Math.Max(best[i, j - 1], dp[i, j]);
            }
        }

        if (best[n, m] <= NegInf)
        {
            return false;
        }

        score = best[n, m];
        AppendRanges(dp, best, text, n, m, ranges, scratch.Positions);
        return true;
    }

    private static void AppendRanges(int[,] dp, int[,] best, string text, int n, int m, List<(int Start, int Length)> ranges, int[] positions)
    {
        var i = n;
        var j = m;

        // Whether the current j is only known as "somewhere achieving best[i, j]" and must be
        // pinned down by walking left (gapped predecessor), vs. an exact dp[i, j] column already
        // known from the consecutive-match branch below (no search needed — searching here would
        // walk past it whenever some other column ties/beats it in best[i, *]).
        var searchMode = true;

        while (i > 0)
        {
            if (searchMode)
            {
                while (j > 0 && best[i, j] == best[i, j - 1])
                {
                    j--;
                }
            }

            positions[i - 1] = j - 1;

            if (i > 1)
            {
                var charScore = BaseMatchScore + BoundaryScore(text, j - 1);
                var isConsecutive = dp[i - 1, j - 1] > NegInf && dp[i, j] == dp[i - 1, j - 1] + charScore + ConsecutiveBonus;
                j = isConsecutive ? j - 1 : j - 2;
                searchMode = !isConsecutive;
            }

            i--;
        }

        var rangeStart = positions[0];
        var rangeLength = 1;
        for (var k = 1; k < n; k++)
        {
            if (positions[k] == positions[k - 1] + 1)
            {
                rangeLength++;
            }
            else
            {
                ranges.Add((rangeStart, rangeLength));
                rangeStart = positions[k];
                rangeLength = 1;
            }
        }

        ranges.Add((rangeStart, rangeLength));
    }

    private static int BoundaryScore(string text, int index)
    {
        if (index == 0)
        {
            return StartOfStringBonus;
        }

        var prev = text[index - 1];
        var current = text[index];

        if (IsSeparator(prev))
        {
            return BoundaryBonus;
        }

        if (char.IsLower(prev) && char.IsUpper(current))
        {
            return BoundaryBonus;
        }

        return 0;
    }

    private static bool IsSeparator(char c) => c is ' ' or '-' or '_' or '.' or '/' or '\\';
}
