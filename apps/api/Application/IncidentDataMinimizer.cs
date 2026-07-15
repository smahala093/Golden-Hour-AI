using System.Text.RegularExpressions;

namespace GoldenHour.Api.Application;

public sealed partial class IncidentDataMinimizer
{
    public string Minimize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var value = EmailPattern().Replace(input, "[email redacted]");
        value = SecretPattern().Replace(value, match => $"{match.Groups[1].Value}[secret redacted]");
        value = LabelledIdentifierPattern().Replace(value, match => $"{match.Groups[1].Value}[identifier redacted]");
        value = LongNumberPattern().Replace(value, "[number redacted]");
        return value;
    }

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\b(password|passcode|secret|api[_ -]?key|bearer)\s*[:=]?\s*[^\s,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"\b(policy|account|member|insurance|aadhaar|passport)\s*(?:number|no\.?|id)?\s*[:#=-]?\s*[A-Z0-9-]{6,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LabelledIdentifierPattern();

    [GeneratedRegex(@"(?<!\w)(?:\+?\d[\d ()-]{7,}\d)(?!\w)", RegexOptions.CultureInvariant)]
    private static partial Regex LongNumberPattern();
}
