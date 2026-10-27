namespace InventorGostSpec.Core.Sorting;

/// <summary>
/// Сравнение строк «как читает человек»: группы цифр сравниваются как числа
/// (АБВГ.301234.002 &lt; АБВГ.301234.010, М8 &lt; М12), регистр не учитывается,
/// латинские буквы, похожие на кириллические (A, B, C, E, H, K, M, O, P, T, X, Y), считаются кириллическими,
/// Ё приравнивается к Е.
/// </summary>
public sealed class NaturalComparer : IComparer<string?>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        x ??= "";
        y ??= "";

        var i = 0;
        var j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var result = CompareNumbers(x, ref i, y, ref j);
                if (result != 0)
                {
                    return result;
                }

                continue;
            }

            var cx = Normalize(x[i]);
            var cy = Normalize(y[j]);
            if (cx != cy)
            {
                return cx.CompareTo(cy);
            }

            i++;
            j++;
        }

        var lengthResult = (x.Length - i).CompareTo(y.Length - j);
        return lengthResult != 0 ? lengthResult : string.CompareOrdinal(x, y);
    }

    /// <summary>Пустые строки идут после непустых.</summary>
    public static int CompareEmptyLast(string? x, string? y)
    {
        var xEmpty = string.IsNullOrWhiteSpace(x);
        var yEmpty = string.IsNullOrWhiteSpace(y);
        if (xEmpty || yEmpty)
        {
            return xEmpty.CompareTo(yEmpty);
        }

        return Instance.Compare(x!.Trim(), y!.Trim());
    }

    private static int CompareNumbers(string x, ref int i, string y, ref int j)
    {
        var startX = SkipZeros(x, i);
        var startY = SkipZeros(y, j);
        var endX = EndOfDigits(x, i);
        var endY = EndOfDigits(y, j);

        var lengthX = endX - startX;
        var lengthY = endY - startY;
        var result = lengthX.CompareTo(lengthY);
        if (result == 0)
        {
            result = string.CompareOrdinal(x, startX, y, startY, lengthX);
        }

        i = endX;
        j = endY;
        return result;
    }

    private static int SkipZeros(string s, int index)
    {
        var end = EndOfDigits(s, index);
        while (index < end - 1 && s[index] == '0')
        {
            index++;
        }

        return index;
    }

    private static int EndOfDigits(string s, int index)
    {
        while (index < s.Length && char.IsDigit(s[index]))
        {
            index++;
        }

        return index;
    }

    internal static char Normalize(char c)
    {
        c = char.ToUpperInvariant(c);
        return c switch
        {
            'A' => 'А',
            'B' => 'В',
            'C' => 'С',
            'E' => 'Е',
            'H' => 'Н',
            'K' => 'К',
            'M' => 'М',
            'O' => 'О',
            'P' => 'Р',
            'T' => 'Т',
            'X' => 'Х',
            'Y' => 'У',
            'Ё' => 'Е',
            _ => c,
        };
    }

    internal static string Normalize(string s)
    {
        var chars = s.Trim().ToCharArray();
        for (var k = 0; k < chars.Length; k++)
        {
            chars[k] = Normalize(chars[k]);
        }

        return new string(chars);
    }
}
