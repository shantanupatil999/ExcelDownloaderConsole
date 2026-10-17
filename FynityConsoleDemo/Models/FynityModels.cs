using System.ComponentModel.DataAnnotations;
using FynityConsoleDemo.Validation;

namespace FynityConsoleDemo.Models;

// ---- Inputs you type into Swagger / Postman to hit this demo API ----

/// <summary>
/// [ApiController] on FynityController runs this validation automatically on
/// every request — an invalid value returns 400 with the attribute's error
/// message, before DownloadReportAsync is ever called.
/// </summary>
public class DownloadRequestDto : IValidatableObject
{
    [Required(ErrorMessage = "SourceId is required.")]
    [AllowedValues("Opnel5aKBz", "yMYerEdOBQ", "4w9aADOevM", "7LDdwpRe1Y",
        ErrorMessage = "SourceId must be one of: Opnel5aKBz, yMYerEdOBQ, 4w9aADOevM, 7LDdwpRe1Y.")]
    public string SourceId { get; set; } = "Opnel5aKBz";

    [Required(ErrorMessage = "SourceType is required.")]
    [AllowedValues("policy", ErrorMessage = "SourceType must be 'policy'.")]
    public string SourceType { get; set; } = "policy";

    [Required(ErrorMessage = "From is required.")]
    [FlexibleDate]
    public string From { get; set; } = "2026-06-01T12:35";

    [Required(ErrorMessage = "To is required.")]
    [FlexibleDate]
    public string To { get; set; } = "2026-06-01T12:35";

    [Required(ErrorMessage = "Type is required.")]
    [AllowedValues("xlsx", ErrorMessage = "Type must be 'xlsx'.")]
    public string Type { get; set; } = "xlsx";

    /// <summary>Cross-field check: From must be earlier than To. Only runs once both
    /// parse as valid dates — if either is malformed, [FlexibleDate] already reported that.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FlexibleDateAttribute.TryParse(From, out var fromDate) &&
            FlexibleDateAttribute.TryParse(To, out var toDate) &&
            fromDate >= toDate)
        {
            yield return new ValidationResult(
                "'From' must be earlier than 'To'.",
                new[] { nameof(From), nameof(To) });
        }
    }
}

public class IssuanceStatusRequestDto
{
    /// <summary>Raw JSON (as a string) you want encrypted and sent as the "Payload" field.</summary>
    public string PayloadJson { get; set; } = "{}";
}

public class EncryptTextRequest
{
    public string PlainText { get; set; } = string.Empty;
}

public class DecryptTextRequest
{
    public string CipherTextBase64 { get; set; } = string.Empty;
}

// ---- Shapes returned by the real Fynity endpoints ----

public class IssuanceStatusEnvelope
{
    public string Payload { get; set; } = string.Empty;
}

public class EncryptedResponseEnvelope
{
    public string encrypted_response { get; set; } = string.Empty;
}

/// <summary>
/// Shape of the JSON you get back AFTER decrypting the download response:
///   { "data": { "link": "https://...&expires=...&signature=...", "content": "base64 xlsx bytes" } }
/// "content" is already the plain xlsx file, just Base64-encoded.
/// </summary>
public class DownloadDecryptedResponse
{
    public DownloadDecryptedData? Data { get; set; }
}

public class DownloadDecryptedData
{
    public string Link { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
