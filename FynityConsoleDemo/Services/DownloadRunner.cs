using System.ComponentModel.DataAnnotations;
using FynityConsoleDemo.Models;

namespace FynityConsoleDemo.Services;

public class DownloadSummary
{
    public string OutputFolder { get; set; } = string.Empty;
    public List<object> Succeeded { get; set; } = new();
    public List<object> Failed { get; set; } = new();
}

/// <summary>
/// Loops over every product in appsettings.json ("Download:Products"), downloads each product's
/// report from Fynity with the same From/To, and saves one xlsx per product into
/// "Download:OutputFolder". If one product fails it is logged (usp_APIExceptionLog) and the
/// loop carries on with the next product.
/// </summary>
public class DownloadRunner
{
    private const string GenericMessage = "Something went wrong. Please try again.";

    private readonly IFynityApiClient _client;
    private readonly IExceptionLogger _exceptionLogger;
    private readonly DownloadSettings _settings;
    private readonly IHostEnvironment _env;

    public DownloadRunner(
        IFynityApiClient client,
        IExceptionLogger exceptionLogger,
        IOptions<DownloadSettings> settings,
        IHostEnvironment env)
    {
        _client = client;
        _exceptionLogger = exceptionLogger;
        _settings = settings.Value;
        _env = env;
    }

    public async Task<DownloadSummary> RunAsync(CancellationToken ct)
    {
        // Relative OutputFolder resolves from the app's own folder (the host's content root is
        // set to AppContext.BaseDirectory in Program.cs), NOT from the working directory —
        // so it behaves the same when Task Scheduler starts the exe from another folder.
        var outputFolder = Path.IsPathRooted(_settings.OutputFolder)
            ? _settings.OutputFolder
            : Path.Combine(_env.ContentRootPath, _settings.OutputFolder);
        Directory.CreateDirectory(outputFolder);
         
        var runStamp = DateTime.Now.ToString("dd-MM-yyyy_HHmmss");
        var summary = new DownloadSummary { OutputFolder = outputFolder };

        foreach (var product in _settings.Products)
        {
            try
            {
                //var toDate = DateTime.Today.AddHours(12); 

                //if (DateTime.Now < toDate)
                //{
                //    toDate = toDate.AddDays(-1); // If job runs before 7 PM
                //}

                //var fromDate = toDate.AddDays(-1); // Previous day 7:00 PM
               

                //var request = new DownloadRequestDto
                //{
                //    SourceId = product.SourceId,
                //    SourceType = product.SourceType,
                //    From = fromDate.ToString("yyyy-MM-ddTHH:mm"),
                //    To = toDate.ToString("yyyy-MM-ddTHH:mm"),
                //    Type = "xlsx"
                //};


                var targetDate = DateTime.Today.AddDays(-1);

                var fromDate = targetDate.Date; // 00:00:00
                var toDate = targetDate.Date.AddHours(23).AddMinutes(59).AddSeconds(59);

                var request = new DownloadRequestDto
                {
                    SourceId = product.SourceId,
                    SourceType = product.SourceType,
                    From = fromDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    To = toDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    Type = "xlsx"
                };



                // Same rules as before (allowed IDs, date format, From < To) —
                // a typo in appsettings.json is reported as a failure for that product only.
                var validationErrors = new List<ValidationResult>();
                if (!Validator.TryValidateObject(request, new ValidationContext(request), validationErrors, validateAllProperties: true))
                {
                    throw new InvalidOperationException(
                        "Invalid settings for product: " + string.Join(" ", validationErrors.Select(e => e.ErrorMessage)));
                }

                var (fileBytes, rawText) = await _client.DownloadReportAsync(request, ct);

                if (fileBytes.Length == 0)
                    throw new InvalidOperationException($"No file returned. Response: {rawText}");

                var fileName = $"{SafeFileName(product.Name)}_{runStamp}.xlsx";
                var filePath = Path.Combine(outputFolder, fileName);
                await File.WriteAllBytesAsync(filePath, fileBytes, ct);

                summary.Succeeded.Add(new { product = product.Name, sourceId = product.SourceId, file = filePath, sizeBytes = fileBytes.Length });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // stopped by the user (Ctrl+C) — nothing to log
            }
            catch (Exception ex)
            {
                // Log it and move on to the next product.
                await _exceptionLogger.LogException(
                    nameof(DownloadRunner), "Download", ex,
                    $"Download failed for product '{product.Name}' (SourceId {product.SourceId})");

                summary.Failed.Add(new { product = product.Name, sourceId = product.SourceId, error = GenericMessage });
            }
        }

        return summary;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Trim().Replace(' ', '_');
    }
}
