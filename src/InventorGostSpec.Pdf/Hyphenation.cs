namespace InventorGostSpec.Pdf;

/// <summary>
/// Перенос русских слов по слогам по правилам П. Христова: «тер-мо-об-ра-бот-ка».
/// С каждой стороны переноса остаётся не меньше двух букв и хотя бы одна гласная;
/// «й», «ь», «ъ» не отрываются от предыдущей буквы.
/// </summary>
public static class Hyphenation
{
    private const string Vowels = "аеёиоуыэюяaeiouy";
    private const string Signs = "йьъ";

    /// <summary>Позиции в слове, перед которыми можно поставить перенос.</summary>
    public static List<int> Points(string word)
    {
        var points = new List<int>();
        for (var p = 2; p <= word.Length - 2; p++)
        {
            if (CanBreak(word, p))
            {
                points.Add(p);
            }
        }

        return points;
    }

    private static bool CanBreak(string w, int p)
    {
        if (!IsLetter(w, p - 1) || !IsLetter(w, p) || Is(w, p, Signs)
            || !HasVowel(w, 0, p) || !HasVowel(w, p, w.Length)
            || CountLetters(w, 0, p) < 2 || CountLetters(w, p, w.Length) < 2)
        {
            return false;
        }

        bool G(int i) => Is(w, i, Vowels);
        bool S(int i) => IsLetter(w, i) && !Is(w, i, Vowels) && !Is(w, i, Signs);

        return Is(w, p - 1, Signs) && IsLetter(w, p + 1)            // й-ll, ь-ll
            || G(p - 2) && S(p - 1) && S(p) && G(p + 1)              // gs-sg
            || S(p - 2) && G(p - 1) && S(p) && G(p + 1)              // sg-sg
            || G(p - 2) && S(p - 1) && S(p) && S(p + 1) && G(p + 2)  // gs-ssg
            || G(p - 3) && S(p - 2) && S(p - 1) && S(p) && S(p + 1) && G(p + 2)              // gss-ssg
            || G(p - 3) && S(p - 2) && S(p - 1) && S(p) && S(p + 1) && S(p + 2) && G(p + 3)  // gss-sssg
            || S(p - 2) && G(p - 1) && G(p) && IsLetter(w, p + 1);   // sg-gl
    }

    private static bool IsLetter(string w, int i) => i >= 0 && i < w.Length && char.IsLetter(w[i]);

    private static bool Is(string w, int i, string set) => i >= 0 && i < w.Length && set.Contains(char.ToLowerInvariant(w[i]));

    private static bool HasVowel(string w, int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            if (Is(w, i, Vowels))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountLetters(string w, int from, int to)
    {
        var count = 0;
        for (var i = from; i < to; i++)
        {
            count += char.IsLetter(w[i]) ? 1 : 0;
        }

        return count;
    }
}
