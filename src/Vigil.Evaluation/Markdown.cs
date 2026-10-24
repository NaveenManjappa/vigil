using System.Globalization;

namespace Vigil.Evaluation;

/// <summary>The formatting every evaluation report shares, so the evaluation and Rule-Tuning Reports read alike.</summary>
internal static class Markdown
{
    public static string Cell(Rate rate) => $"{rate.Count} / {rate.Of} ({Percent(rate)})";

    // Invariant culture, so a report reads the same whatever machine generated it.
    public static string Percent(Rate rate) =>
        rate.Value is { } v ? (v * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "n/a";

    public static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    // Model-written text can hold a pipe or a line break, either of which would break the table row.
    public static string Escape(string text) => text.Replace("|", "\\|").ReplaceLineEndings(" ");
}
