using System.Text.Json;
using FynityConsoleDemo.Models;
using FynityConsoleDemo.Services;

// Exit codes (so Task Scheduler / scripts can tell what happened):
const int ExitOk = 0;        // everything succeeded
const int ExitPartial = 1;   // download: some products saved, some failed (same as the API's 207)
const int ExitFailed = 2;    // download: every product failed, or an unexpected error (same as the API's 500)
const int ExitUsage = 3;     // bad command line

const string GenericMessage = "Something went wrong. Please try again.";

// ContentRootPath = the exe's own folder, so appsettings.json is found (and a relative
// "Download:OutputFolder" resolves) no matter which folder the program is started from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.Configure<CryptoOptions>(builder.Configuration.GetSection("Crypto"));
builder.Services.Configure<FynityOptions>(builder.Configuration.GetSection("Fynity"));
builder.Services.Configure<DownloadSettings>(builder.Configuration.GetSection("Download"));

builder.Services.AddSingleton<IExceptionLogger, ExceptionLogger>();
builder.Services.AddSingleton<IAesEncryptionService, AesEncryptionService>();
builder.Services.AddHttpClient<IFynityApiClient, FynityApiClient>();
builder.Services.AddTransient<DownloadRunner>();

using var host = builder.Build();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var ct = cts.Token;

var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

// No arguments = the download (what GET/POST api/fynity/download used to do).
var command = args.Length == 0 ? "download" : args[0].ToLowerInvariant();

try
{
    switch (command)
    {
        case "download":
        {
            var summary = await host.Services.GetRequiredService<DownloadRunner>().RunAsync(ct);
            Console.WriteLine(JsonSerializer.Serialize(summary, jsonOptions));

            if (summary.Failed.Count == 0) return ExitOk;
            return summary.Succeeded.Count == 0 ? ExitFailed : ExitPartial;
        }

        case "issuance-status": // was POST api/fynity/issuance-status
        {
            var payloadJson = args.Length > 1 ? args[1] : "{}";
            var decrypted = await host.Services.GetRequiredService<IFynityApiClient>().GetIssuanceStatusAsync(payloadJson, ct);
            Console.WriteLine(JsonSerializer.Serialize(new { decrypted }, jsonOptions));
            return ExitOk;
        }

        case "encrypt": // was POST api/crypto/encrypt
        {
            if (args.Length < 2) return Usage();
            var cipher = host.Services.GetRequiredService<IAesEncryptionService>().Encrypt(args[1]);
            Console.WriteLine(JsonSerializer.Serialize(new { cipherTextBase64 = cipher }, jsonOptions));
            return ExitOk;
        }

        case "decrypt": // was POST api/crypto/decrypt
        {
            if (args.Length < 2) return Usage();
            var plain = host.Services.GetRequiredService<IAesEncryptionService>().Decrypt(args[1]);
            Console.WriteLine(JsonSerializer.Serialize(new { plainText = plain }, jsonOptions));
            return ExitOk;
        }

        default:
            return Usage();
    }
}
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled.");
    return ExitFailed;
}
catch (Exception ex)
{
    // Replaces the API's global exception handler: log it, show only the generic message,
    // in the same { "errors": { "Error": [...] } } shape the API returned.
    try
    {
        await host.Services.GetRequiredService<IExceptionLogger>().LogException(
            "Program", command, ex, $"Unhandled exception running command '{command}'");
    }
    catch
    {
        // logging must never stop the generic message below from being shown
    }

    Console.WriteLine(JsonSerializer.Serialize(
        new { errors = new Dictionary<string, string[]> { ["Error"] = new[] { GenericMessage } } }, jsonOptions));
    return ExitFailed;
}

int Usage()
{
    Console.Error.WriteLine(
        """
        Usage:
          FynityConsoleDemo                          download all products (default)
          FynityConsoleDemo download                 same as above
          FynityConsoleDemo issuance-status "<json>" send an encrypted payload to issuance-status
          FynityConsoleDemo encrypt "<text>"         encrypt text with the configured key
          FynityConsoleDemo decrypt "<base64>"       decrypt text with the configured key
        """);
    return ExitUsage;
}
