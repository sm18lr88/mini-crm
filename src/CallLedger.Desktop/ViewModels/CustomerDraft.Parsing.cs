using System.Globalization;

namespace CallLedger.Desktop.ViewModels;

public sealed partial class CustomerDraft
{
    private const string CanonicalDateFormat = "yyyy-MM-dd";

    private static bool TryParseDate(string value, out DateOnly? date)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            date = null;
            return true;
        }

        var trimmedValue = value.Trim();
        if (HasIsoDatePrefix(trimmedValue))
        {
            if (DateOnly.TryParseExact(trimmedValue, CanonicalDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var canonicalDate))
            {
                date = canonicalDate;
                return true;
            }

            date = null;
            return false;
        }

        if (DateOnly.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
            return true;
        }

        date = null;
        return false;
    }

    private static bool TryParseFollowUp(string value, out DateTimeOffset? followUpAt)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            followUpAt = null;
            return true;
        }

        var trimmedValue = value.Trim();
        DateTime parsed;
        if (HasIsoDatePrefix(trimmedValue))
        {
            if (!DateTime.TryParse(trimmedValue, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
            {
                followUpAt = null;
                return false;
            }
        }
        else if (!DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
        {
            followUpAt = null;
            return false;
        }

        var localTime = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        var timeZone = TimeZoneInfo.Local;
        if (timeZone.IsInvalidTime(localTime))
        {
            followUpAt = null;
            return false;
        }

        var offset = timeZone.IsAmbiguousTime(localTime)
            ? timeZone.GetAmbiguousTimeOffsets(localTime).Min()
            : timeZone.GetUtcOffset(localTime);
        followUpAt = new DateTimeOffset(localTime, offset);
        return true;
    }

    private static bool HasIsoDatePrefix(string value)
        => value.Length >= CanonicalDateFormat.Length
            && value[4] == '-'
            && value[7] == '-'
            && HasAsciiDigits(value.AsSpan(0, 4))
            && HasAsciiDigits(value.AsSpan(5, 2))
            && HasAsciiDigits(value.AsSpan(8, 2));

    private static bool HasAsciiDigits(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
