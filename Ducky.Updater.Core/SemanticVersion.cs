using System.Globalization;

namespace Ducky.Updater.Core;

public readonly record struct SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    string? PreRelease = null) : IComparable<SemanticVersion>
{
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
            normalized = normalized[1..];

        int buildSeparator = normalized.IndexOf('+');
        if (buildSeparator >= 0)
            normalized = normalized[..buildSeparator];

        int preReleaseSeparator = normalized.IndexOf('-');
        string? preRelease = preReleaseSeparator >= 0
            ? normalized[(preReleaseSeparator + 1)..]
            : null;
        string core = preReleaseSeparator >= 0
            ? normalized[..preReleaseSeparator]
            : normalized;
        string[] components = core.Split('.');
        if (components.Length != 3 ||
            !TryParseNumber(components[0], out int major) ||
            !TryParseNumber(components[1], out int minor) ||
            !TryParseNumber(components[2], out int patch) ||
            (preRelease is not null && !IsValidPreRelease(preRelease)))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        int comparison = Major.CompareTo(other.Major);
        if (comparison != 0)
            return comparison;

        comparison = Minor.CompareTo(other.Minor);
        if (comparison != 0)
            return comparison;

        comparison = Patch.CompareTo(other.Patch);
        if (comparison != 0)
            return comparison;

        if (PreRelease is null)
            return other.PreRelease is null ? 0 : 1;
        if (other.PreRelease is null)
            return -1;

        string[] left = PreRelease.Split('.');
        string[] right = other.PreRelease.Split('.');
        for (int index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            bool leftNumeric = IsNumericIdentifier(left[index]);
            bool rightNumeric = IsNumericIdentifier(right[index]);
            if (leftNumeric && rightNumeric)
            {
                comparison = CompareNumericIdentifiers(left[index], right[index]);
            }
            else if (leftNumeric != rightNumeric)
            {
                comparison = leftNumeric ? -1 : 1;
            }
            else
            {
                comparison = string.CompareOrdinal(left[index], right[index]);
            }

            if (comparison != 0)
                return comparison;
        }

        return left.Length.CompareTo(right.Length);
    }

    public override string ToString() =>
        PreRelease is null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{Major}.{Minor}.{Patch}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{Major}.{Minor}.{Patch}-{PreRelease}");

    private static bool TryParseNumber(string value, out int number)
    {
        number = 0;
        return value.Length > 0 &&
               (value.Length == 1 || value[0] != '0') &&
               int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static bool IsValidPreRelease(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (string identifier in value.Split('.'))
        {
            if (identifier.Length == 0)
                return false;

            foreach (char character in identifier)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character != '-')
                    return false;
            }

            if (IsNumericIdentifier(identifier) &&
                identifier.Length > 1 &&
                identifier[0] == '0')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNumericIdentifier(string value) =>
        value.All(character => character is >= '0' and <= '9');

    private static int CompareNumericIdentifiers(string left, string right)
    {
        int comparison = left.Length.CompareTo(right.Length);
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(left, right);
    }
}
