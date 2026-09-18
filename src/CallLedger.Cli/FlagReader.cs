using CallLedger.Core;

namespace CallLedger.Cli;

internal sealed class FlagReader
{
    private readonly List<string> remaining;
    private readonly HashSet<string> read = new(StringComparer.Ordinal);

    public FlagReader(IEnumerable<string> args) => remaining = args.ToList();

    public string? InputPath { get; private set; }
    public string? OutputPath { get; private set; }
    public long? ExpectedRevision { get; private set; }
    public CallFilter Filter { get; private set; } = CallFilter.All;
    public int Offset { get; private set; }
    public int Limit { get; private set; } = 50;
    public bool Reveal { get; private set; }
    public bool Redact { get; private set; }
    public bool Yes { get; private set; }

    public void ReadInput(bool required)
    {
        InputPath = ReadValue("--input");
        if (required && InputPath is null)
        {
            throw new CliException(2, "USAGE", "This command requires --input - or --input PATH.");
        }
    }

    public void ReadOutput() => OutputPath = ReadValue("--output") ?? throw new CliException(2, "USAGE", "Backup requires --output PATH.");

    public void ReadExpectedRevision()
    {
        var value = ReadValue("--expected-revision");
        if (value is not null && (!long.TryParse(value, out var revision) || revision < 0))
        {
            throw new CliException(2, "USAGE", "Expected revision must be a non-negative integer.", new { fields = new[] { "expectedRevision" } });
        }

        ExpectedRevision = value is null ? null : long.Parse(value);
    }

    public void ReadListOptions()
    {
        var filter = ReadValue("--filter");
        if (filter is not null)
        {
            Filter = filter switch
            {
                "all" => CallFilter.All,
                "due" => CallFilter.Due,
                "upcoming" => CallFilter.Upcoming,
                "unscheduled" => CallFilter.Unscheduled,
                _ => throw new CliException(2, "USAGE", "Filter must be all, due, upcoming, or unscheduled.", new { fields = new[] { "filter" } })
            };
        }

        Offset = ReadBoundedInt("--offset", 0, int.MaxValue, 0);
        Limit = ReadBoundedInt("--limit", 1, 200, 50);
    }

    public void ReadCustomerDisplay()
    {
        Reveal = ReadBoolean("--reveal");
        Redact = ReadBoolean("--redact");
        if (Reveal && Redact)
        {
            throw new CliException(2, "USAGE", "--reveal and --redact cannot be combined.");
        }
    }

    public void ReadYes() => Yes = ReadBoolean("--yes");

    public void EnsureComplete()
    {
        if (remaining.Count != 0)
        {
            throw new CliException(2, "USAGE", "Command contains unsupported or extra arguments.");
        }
    }

    private string? ReadValue(string flag)
    {
        var index = remaining.IndexOf(flag);
        if (index < 0)
        {
            return null;
        }

        if (!read.Add(flag) || index + 1 >= remaining.Count || remaining[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliException(2, "USAGE", "Command option is missing a value.");
        }

        var value = remaining[index + 1];
        remaining.RemoveAt(index + 1);
        remaining.RemoveAt(index);
        return value;
    }

    private bool ReadBoolean(string flag)
    {
        var index = remaining.IndexOf(flag);
        if (index < 0)
        {
            return false;
        }

        if (!read.Add(flag))
        {
            throw new CliException(2, "USAGE", "Command option is duplicated.");
        }

        remaining.RemoveAt(index);
        return true;
    }

    private int ReadBoundedInt(string flag, int minimum, int maximum, int defaultValue)
    {
        var value = ReadValue(flag);
        if (value is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out var parsed) || parsed < minimum || parsed > maximum)
        {
            throw new CliException(2, "USAGE", "Paging value is out of range.", new { fields = new[] { flag[2..] } });
        }

        return parsed;
    }
}
