namespace WordStrip.Core.Prediction;

/// <summary>
/// Bounded restricted Damerau-Levenshtein edit distance (handles adjacent transpositions in addition to
/// insert/delete/substitute). Bounded so callers can cheaply reject candidates beyond a max distance
/// without paying for the full O(n*m) table when the strings are wildly different lengths.
/// </summary>
internal static class DamerauLevenshtein
{
    public static int Distance(string a, string b, int maxDistance)
    {
        if (a == b) return 0;

        var lenA = a.Length;
        var lenB = b.Length;
        if (Math.Abs(lenA - lenB) > maxDistance) return maxDistance + 1;
        if (lenA == 0) return lenB <= maxDistance ? lenB : maxDistance + 1;
        if (lenB == 0) return lenA <= maxDistance ? lenA : maxDistance + 1;

        var d = new int[lenA + 1, lenB + 1];
        for (var i = 0; i <= lenA; i++) d[i, 0] = i;
        for (var j = 0; j <= lenB; j++) d[0, j] = j;

        for (var i = 1; i <= lenA; i++)
        {
            var rowMin = int.MaxValue;
            for (var j = 1; j <= lenB; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var value = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);

                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    value = Math.Min(value, d[i - 2, j - 2] + 1);
                }

                d[i, j] = value;
                if (value < rowMin) rowMin = value;
            }

            if (rowMin > maxDistance) return maxDistance + 1;
        }

        return d[lenA, lenB];
    }
}
