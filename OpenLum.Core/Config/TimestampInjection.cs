using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenLum.Console.Config;

/// <summary>
/// Injects current date/time prefix into user messages so the model has accurate time awareness.
/// Aligns with OpenLum's agent-timestamp: [Dow YYYY-MM-DD HH:mm TZ] message.
/// </summary>
public static class TimestampInjection
{
    /// <summary>Matches leading [Dow YYYY-MM-DD HH:mm ...] envelope to avoid double-stamping.</summary>
    private static readonly Regex EnvelopePattern = new(@"^\[.*\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}", RegexOptions.Compiled);

    /// <summary>
    /// Prepends a compact timestamp envelope if the message doesn't already have one.
    /// </summary>
    /// <param name="message">User message.</param>
    /// <param name="timeZoneId">IANA or Windows timezone ID. Null = local.</param>
    /// <param name="now">Override for testing. Null = DateTime.Now.</param>
    /// <returns>Message with timestamp prefix when applicable.</returns>
    public static string Inject(string message, string? timeZoneId = null, DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        if (EnvelopePattern.IsMatch(message))
            return message;

        var tz = ResolveTimeZone(timeZoneId);
        var dt = now ?? DateTime.Now;
        var zoned = TimeZoneInfo.ConvertTime(dt, tz);
        var offset = tz.GetUtcOffset(zoned);
        var offsetStr = (offset >= TimeSpan.Zero ? "+" : "-") + offset.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

        var dow = zoned.ToString("ddd", CultureInfo.InvariantCulture);
        var dateTime = zoned.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        return $"[{dow} {dateTime} {offsetStr}] {message}";
    }

    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return TimeZoneInfo.Local;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
