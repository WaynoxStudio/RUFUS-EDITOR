using System.Text.RegularExpressions;

namespace RufusMapEditor.LegacyCompatibility.Logging;

/// <summary>
/// Strips credentials / secrets from log messages. Never logs passwords or full connection strings.
/// </summary>
public static partial class LogMessageSanitizer
{
    public static string Sanitize(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return "";

        var s = message;

        s = PasswordAssignmentRegex().Replace(s, "$1=***");
        s = UriUserInfoRegex().Replace(s, "$1:***@");
        s = ConnectionStringPwdRegex().Replace(s, "$1=***");
        s = MysqlUriPasswordRegex().Replace(s, "$1***$2");
        s = ProtectedBase64Regex().Replace(s, "$1=***");

        return s;
    }

    [GeneratedRegex(
        @"(?i)(password|passwd|pwd|passphrase|secret|api[_-]?key|token|credential)(\s*)(=|:)\s*[^\s;,""']+",
        RegexOptions.CultureInvariant)]
    private static partial Regex PasswordAssignmentRegex();

    [GeneratedRegex(
        @"(?i)(Pwd|Password|PasswordProtectedBase64)\s*=\s*[^\s;]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringPwdRegex();

    /// <summary>user:password@host → user:***@host</summary>
    [GeneratedRegex(
        @"([^\s:/@]+):([^\s/@]+)@",
        RegexOptions.CultureInvariant)]
    private static partial Regex UriUserInfoRegex();

    [GeneratedRegex(
        @"(?i)(mysql(?:\+ssh)?://[^:/]+:)([^@/\s]+)(@)",
        RegexOptions.CultureInvariant)]
    private static partial Regex MysqlUriPasswordRegex();

    [GeneratedRegex(
        @"(?i)(PasswordProtectedBase64|PasswordProtected)\s*=\s*[^\s;,""']+",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProtectedBase64Regex();
}
