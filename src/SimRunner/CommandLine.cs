using System.Globalization;

namespace SimRunner;

public sealed class UsageException(string message) : Exception(message);

// Flags after the command name. Anything unexpected is a usage error, never a silent default.
public sealed class CommandLine
{
    private readonly Dictionary<string, string> _values = [];
    private readonly HashSet<string> _switches = [];

    public CommandLine(IReadOnlyList<string> args, IReadOnlyCollection<string> valueFlags, IReadOnlyCollection<string> switches)
    {
        for (int i = 0; i < args.Count; i++)
        {
            string flag = args[i];
            if (switches.Contains(flag))
            {
                _switches.Add(flag);
            }
            else if (valueFlags.Contains(flag))
            {
                if (i + 1 >= args.Count)
                {
                    throw new UsageException($"{flag} needs a value.");
                }

                _values[flag] = args[++i];
            }
            else
            {
                throw new UsageException($"Unknown option '{flag}'.");
            }
        }
    }

    public bool Has(string flag)
    {
        return _switches.Contains(flag) || _values.ContainsKey(flag);
    }

    public string Text(string flag, string fallback)
    {
        return _values.GetValueOrDefault(flag, fallback);
    }

    public int Int(string flag, int fallback, int min = int.MinValue, int max = int.MaxValue)
    {
        if (!_values.TryGetValue(flag, out string? text))
        {
            return fallback;
        }

        if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) && value >= min && value <= max)
        {
            return value;
        }

        throw new UsageException(min == int.MinValue ? $"{flag} must be a whole number, not '{text}'." : $"{flag} must be a whole number from {min} to {max}, not '{text}'.");
    }

    public ulong Seed(string flag, ulong fallback)
    {
        if (!_values.TryGetValue(flag, out string? text))
        {
            return fallback;
        }

        return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value) ? value : throw new UsageException($"{flag} must be a whole number of 0 or more, not '{text}'.");
    }

    public T Choice<T>(string flag, T fallback)
        where T : struct, Enum
    {
        if (!_values.TryGetValue(flag, out string? text))
        {
            return fallback;
        }

        foreach (T value in Enum.GetValues<T>())
        {
            if (string.Equals(Kebab(value.ToString()), text, StringComparison.Ordinal))
            {
                return value;
            }
        }

        throw new UsageException($"{flag} must be one of {string.Join(", ", Enum.GetValues<T>().Select(v => Kebab(v.ToString())))}, not '{text}'.");
    }

    private static string Kebab(string name)
    {
        return string.Concat(name.Select((c, i) => char.IsUpper(c) ? (i > 0 ? "-" : "") + char.ToLowerInvariant(c) : c.ToString()));
    }
}
