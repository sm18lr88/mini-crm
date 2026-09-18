using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CallLedger.Cli;

internal sealed class JsonObjectInput
{
    private static readonly Regex TimestampPattern = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$",
        RegexOptions.CultureInvariant);
    private readonly IReadOnlyDictionary<string, JsonElement> values;

    public JsonObjectInput(IReadOnlyDictionary<string, JsonElement> values) => this.values = values;

    public int Count => values.Count;

    public void RequireKnown(params string[] names)
    {
        foreach (var name in values.Keys)
        {
            if (!names.Contains(name, StringComparer.Ordinal))
            {
                throw new CliException(2, "INVALID_JSON", "Input contains an unsupported property.");
            }
        }
    }

    public OptionalValue<string> ReadText(string name, bool nullable)
    {
        if (!values.TryGetValue(name, out var value))
        {
            return new(false, null);
        }

        if (value.ValueKind == JsonValueKind.Null && nullable)
        {
            return new(true, null);
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw InvalidField(name);
        }

        return new(true, value.GetString());
    }

    public OptionalValue<DateOnly?> ReadDate(string name)
    {
        if (!values.TryGetValue(name, out var value))
        {
            return new(false, null);
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return new(true, null);
        }

        if (value.ValueKind != JsonValueKind.String || !DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw InvalidField(name);
        }

        return new(true, parsed);
    }

    public OptionalValue<DateTimeOffset?> ReadTimestamp(string name)
    {
        if (!values.TryGetValue(name, out var value))
        {
            return new(false, null);
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return new(true, null);
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw InvalidField(name);
        }

        var text = value.GetString();
        if (text is null || !TimestampPattern.IsMatch(text)
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw InvalidField(name);
        }

        return new(true, parsed.ToUniversalTime());
    }

    public OptionalValue<bool> ReadBoolean(string name)
    {
        if (!values.TryGetValue(name, out var value))
        {
            return new(false, false);
        }

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw InvalidField(name);
        }

        return new(true, value.GetBoolean());
    }

    private static CliException InvalidField(string name) => new(2, "INVALID_JSON", "Input field has an invalid type or format.", new { fields = new[] { name } });
}
