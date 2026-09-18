using System.Globalization;
using System.Text;
using System.Text.Json;
using CallLedger.Core;

namespace CallLedger.Cli;

internal static class CliJson
{
    private const int MaximumInputBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static JsonObjectInput ReadObject(string inputPath, TextReader input)
    {
        var utf8 = RemoveByteOrderMark(ReadUtf8(inputPath, input));
        try
        {
            using var document = JsonDocument.Parse(utf8, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw InvalidJson();
            }

            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!values.TryAdd(property.Name, property.Value.Clone()))
                {
                    throw new CliException(2, "INVALID_JSON", "Input JSON contains duplicate properties.");
                }
            }

            return new JsonObjectInput(values);
        }
        catch (JsonException)
        {
            throw InvalidJson();
        }
    }

    public static CustomerPatch ReadCustomerPatch(string inputPath, TextReader input, bool allowEmpty)
    {
        var value = ReadObject(inputPath, input);
        value.RequireKnown("name", "dateOfBirth", "ssn", "medicareId", "phone", "followUpAt", "callReason", "notes", "lastCalledAt");
        if (!allowEmpty && value.Count == 0)
        {
            throw new CliException(2, "INVALID_JSON", "Customer patch must contain at least one field.");
        }

        return new CustomerPatch(
            value.ReadText("name", nullable: false),
            value.ReadDate("dateOfBirth"),
            value.ReadText("ssn", nullable: true),
            value.ReadText("medicareId", nullable: true),
            value.ReadText("phone", nullable: true),
            value.ReadTimestamp("followUpAt"),
            value.ReadText("callReason", nullable: true),
            value.ReadText("notes", nullable: true),
            value.ReadTimestamp("lastCalledAt"));
    }

    public static MarkCalledPatch ReadMarkCalledPatch(string inputPath, TextReader input)
    {
        var value = ReadObject(inputPath, input);
        value.RequireKnown("at", "followUpAt", "notes", "callReason");
        return new MarkCalledPatch(value.ReadTimestamp("at"), value.ReadTimestamp("followUpAt"), value.ReadText("notes", nullable: true), value.ReadText("callReason", nullable: true));
    }

    public static PreferencesPatch ReadPreferencesPatch(string inputPath, TextReader input)
    {
        var value = ReadObject(inputPath, input);
        value.RequireKnown("showSensitiveByDefault", "compactMode", "alwaysOnTop");
        if (value.Count == 0)
        {
            throw new CliException(2, "INVALID_JSON", "Preference patch must contain at least one field.");
        }

        return new PreferencesPatch(value.ReadBoolean("showSensitiveByDefault"), value.ReadBoolean("compactMode"), value.ReadBoolean("alwaysOnTop"));
    }

    public static string ReadSearchQuery(string inputPath, TextReader input)
    {
        var value = ReadObject(inputPath, input);
        value.RequireKnown("query");
        var query = value.ReadText("query", nullable: false);
        if (!query.IsPresent)
        {
            throw new CliException(2, "INVALID_JSON", "Search input requires query.", new { fields = new[] { "query" } });
        }

        return query.Value!;
    }

    public static CustomerOutput ToOutput(Customer customer, bool reveal) => new(
        customer.Id,
        customer.Name,
        reveal ? customer.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
        reveal ? customer.Ssn : null,
        reveal ? customer.MedicareId : null,
        customer.Phone,
        FormatTimestamp(customer.FollowUpAt),
        customer.CallReason,
        customer.Notes,
        FormatTimestamp(customer.LastCalledAt));

    public static string Serialize(object value) => JsonSerializer.Serialize(value, SerializerOptions);

    private static byte[] ReadUtf8(string inputPath, TextReader input)
    {
        try
        {
            if (inputPath != "-")
            {
                using var file = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var content = new MemoryStream();
                var fileBuffer = new byte[4096];
                int fileRead;
                while ((fileRead = file.Read(fileBuffer, 0, fileBuffer.Length)) > 0)
                {
                    content.Write(fileBuffer, 0, fileRead);
                    if (content.Length > MaximumInputBytes)
                    {
                        throw new CliException(2, "INPUT_LIMIT", "Input exceeds the 1 MiB limit.");
                    }
                }

                return content.ToArray();
            }

            var builder = new StringBuilder();
            var buffer = new char[4096];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                builder.Append(buffer, 0, read);
                if (Encoding.UTF8.GetByteCount(builder.ToString()) > MaximumInputBytes)
                {
                    throw new CliException(2, "INPUT_LIMIT", "Input exceeds the 1 MiB limit.");
                }
            }

            return Encoding.UTF8.GetBytes(builder.ToString());
        }
        catch (CliException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new CliException(6, "SAFE_IO", "Input file could not be read.");
        }
    }

    private static byte[] RemoveByteOrderMark(byte[] utf8) => utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF ? utf8[3..] : utf8;
    private static CliException InvalidJson() => new(2, "INVALID_JSON", "Input must be a bounded JSON object with valid field types.");
    private static string? FormatTimestamp(DateTimeOffset? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
