using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace FynityConsoleDemo.Validation;

/// <summary>
/// Validates that a string is a recognizable date (or date-time). Deliberately
/// accepts a wide set of common formats — yyyy-MM-dd, dd-MM-yyyy, dd-MMM-yyyy,
/// yyyy-MM-ddTHH:mm, etc. — since "From"/"To" have been sent in more than one
/// shape already. Add more to KnownFormats below if another format shows up.
/// </summary>
public class FlexibleDateAttribute : ValidationAttribute
{
    // Tried first, in order, with DateTime.TryParseExact — fast and unambiguous
    // (no "is 01-02-2026 Jan 2nd or Feb 1st" guessing).
    private static readonly string[] KnownFormats =
    {
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MMM-dd",
        "yyyy/MM/dd",
        "dd-MM-yyyy",
        "dd-MM-yyyy HH:mm",
        "dd-MMM-yyyy",
        "dd-MMM-yyyy HH:mm",
        "dd/MM/yyyy",
        "MM/dd/yyyy",
        "MM-dd-yyyy",
    };

    public FlexibleDateAttribute()
    {
        ErrorMessage = "'{0}' is not a recognized date. Use a format like yyyy-MM-dd, dd-MM-yyyy, dd-MMM-yyyy, or yyyy-MM-ddTHH:mm.";
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            // Let [Required] report missing values; an empty string here is not this attribute's concern.
            return ValidationResult.Success;
        }

        return TryParse(text, out _)
            ? ValidationResult.Success
            : new ValidationResult(FormatErrorMessage(validationContext.DisplayName), new[] { validationContext.MemberName ?? string.Empty });
    }

    /// <summary>
    /// Shared with the DTO's cross-field "From must be before To" check, so both
    /// use exactly the same parsing rules.
    /// </summary>
    public static bool TryParse(string? text, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (DateTime.TryParseExact(text, KnownFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return true;

        // Fallback: accept anything else .NET can confidently parse (e.g. full
        // ISO 8601 with seconds/offset) without accepting arbitrary garbage text.
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }
}
