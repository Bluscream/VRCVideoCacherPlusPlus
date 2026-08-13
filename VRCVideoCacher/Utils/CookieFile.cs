namespace VRCVideoCacher.Utils;

// Pure parsing helpers for the Netscape-format cookie file yt-dlp consumes.
// Kept free of file/static state so the parsing rules can be tested directly.
public static class CookieFile
{
    // Netscape columns: domain, includeSubdomains, path, secure, expiry, name, value
    private const int ExpiryColumn = 4;
    private const int NameColumn = 5;
    private const int MinColumns = 7;

    // LOGIN_INFO is the cookie that carries the YouTube login — it is what IsCookiesValid
    // keys on, and once it lapses the jar stops authenticating regardless of the rest.
    public const string LoginCookieName = "LOGIN_INFO";

    // Returns the LOGIN_INFO expiry, or null when there is no such line, it is a session
    // cookie (expiry 0), or the value doesn't parse. Malformed lines are skipped, matching
    // how ValidateCookiesAsync tolerates them.
    public static DateTime? ParseLoginExpiryUtc(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var parts = line.Split('\t');
            if (parts.Length < MinColumns || parts[NameColumn] != LoginCookieName)
                continue;

            if (!long.TryParse(parts[ExpiryColumn], out var unix) || unix <= 0)
                continue;

            return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
        }

        return null;
    }
}
