namespace wa_api.Features.Contacts;

/// <summary>
/// Normalizes and validates WhatsApp phone numbers to the storage convention: E.164 digits only,
/// no leading '+' (e.g. <c>"+94 77 123 4567"</c> → <c>"94771234567"</c>). Meta requires E.164, and
/// clean/valid numbers keep contact counts — and now quota — honest.
/// </summary>
public static class PhoneNumber
{
    // E.164 allows at most 15 digits (country code + subscriber). A realistic floor for a
    // country-code-prefixed mobile number is ~8 digits; shorter almost always means a national
    // number missing its country code, which we can't safely infer.
    private const int MinDigits = 8;
    private const int MaxDigits = 15;

    /// <summary>
    /// Normalizes <paramref name="raw"/> to E.164 digits-only and validates it. Strips '+', spaces and
    /// separators, and a leading international <c>00</c> prefix. Returns false (with an empty result) for
    /// numbers that can't be a valid E.164 number: empty, wrong length, or in national format (leading 0,
    /// i.e. missing a country code) — which we reject rather than guess a country for.
    /// </summary>
    public static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var digits = new string(raw.Where(char.IsDigit).ToArray());

        // "00" is the international call prefix (e.g. 0094… → 94…) — drop it so the country code leads.
        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];

        if (digits.Length < MinDigits || digits.Length > MaxDigits)
            return false;

        // A country code never starts with 0; a leading 0 means a national/trunk number with no country
        // code, which can't be turned into E.164 without knowing the country.
        if (digits[0] == '0')
            return false;

        normalized = digits;
        return true;
    }
}
