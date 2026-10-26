using System.Text;
using System.Text.Json;

namespace Eco.Minecarts.Runtime;

// Existing saves are literal comma-separated names, including literal quotes.
// Only lists that cannot use that representation receive an explicit version.
// This keeps old names intact without adding a serialized field to either tab.
internal static class TramNameList
{
    // Legacy editors removed empty separators before joining names, so their
    // saved values cannot start with this pair of separators. A legacy name
    // that happens to look like our version label must still remain literal.
    private const string Prefix = ",,\u001eRailworks.TramNames/1:";

    internal static string[] Read(string source)
    {
        if (source.StartsWith(Prefix, StringComparison.Ordinal))
        {
            try
            {
                var names = JsonSerializer.Deserialize<string[]>(source.AsSpan(Prefix.Length));
                if (names is { Length: > 0 } && names.All(name => !string.IsNullOrWhiteSpace(name))) return names;
            }
            catch (JsonException) { }
        }
        return source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    internal static string Write(string[] names)
    {
        var legacy = string.Join(", ", names);
        return names.Any(name => name.Contains(',')) || legacy.StartsWith(Prefix, StringComparison.Ordinal)
            ? Prefix + JsonSerializer.Serialize(names) : legacy;
    }

    internal static string Format(string[] names) => string.Join(", ", names.Select(name =>
        name.Contains(',') || name.StartsWith('"') || names.Length == 1 && name.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? "\"" + name.Replace("\"", "\"\"") + "\"" : name));

    // Bare 'all' is the existing command. Quoted "all" is one literal name.
    // Empty separators remain tolerated; malformed quoted fields never clear a list.
    internal static bool TryParse(string input, out string[] names)
    {
        names = [];
        if (input.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)) return true;
        var parsed = new List<string>();
        var position = 0;
        while (position < input.Length)
        {
            while (position < input.Length && (char.IsWhiteSpace(input[position]) || input[position] == ',')) position++;
            if (position == input.Length) break;
            string name;
            if (input[position] == '"')
            {
                position++;
                var value = new StringBuilder();
                var closed = false;
                while (position < input.Length)
                {
                    var character = input[position++];
                    if (character != '"') { value.Append(character); continue; }
                    if (position < input.Length && input[position] == '"') { value.Append('"'); position++; continue; }
                    closed = true;
                    break;
                }
                if (!closed) return false;
                while (position < input.Length && char.IsWhiteSpace(input[position])) position++;
                if (position < input.Length && input[position] != ',') return false;
                name = value.ToString().Trim();
                if (name.Length == 0) return false;
            }
            else
            {
                var start = position;
                while (position < input.Length && input[position] != ',') position++;
                name = input[start..position].Trim();
            }
            if (name.Length > 0) parsed.Add(name);
        }
        if (parsed.Count == 0) return false;
        names = parsed.ToArray();
        return true;
    }
}
