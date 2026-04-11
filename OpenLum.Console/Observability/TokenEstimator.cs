namespace OpenLum.Console.Observability;

/// <summary>Rough token estimates when the API does not return usage (chars/4 heuristic).</summary>
public static class TokenEstimator
{
    public static int RoughTokensFromChars(int charCount) =>
        charCount <= 0 ? 0 : Math.Max(1, charCount / 4);

    public static int RoughTokensFromText(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : RoughTokensFromChars(text.Length);
}
