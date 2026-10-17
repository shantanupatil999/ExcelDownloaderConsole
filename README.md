# Fynity Console Demo

Console version of the Fynity API project. Same code for encryption (AES-256-GCM), the Fynity client,
validation, the product list in `appsettings.json`, saving one xlsx per product, and logging failures to
`usp_APIExceptionLog` — only the way it is started and how results are shown changed.

## Running

```
FynityConsoleDemo.exe                            download all products (default)
FynityConsoleDemo.exe download                   same
FynityConsoleDemo.exe issuance-status "<json>"   was POST api/fynity/issuance-status
FynityConsoleDemo.exe encrypt "<text>"           was POST api/crypto/encrypt
FynityConsoleDemo.exe decrypt "<base64>"         was POST api/crypto/decrypt
```

`download` loops over `Download:Products`, saves one `<ProductName>_<SourceId>_<yyyyMMdd_HHmmss>.xlsx` per product
into `Download:OutputFolder`, carries on if a product fails, and prints the same summary JSON the API returned
(`outputFolder`, `succeeded`, `failed`).

A relative `OutputFolder` (default `Downloads`) is resolved from the folder the exe lives in — not from the
working directory — so it is the same when Task Scheduler starts it. An absolute path (e.g. `D:\FynityDownloads`) also works.
`appsettings.json` is also read from the exe's folder.

## Exit codes
| Code | Meaning |
|------|---------|
| 0 | all succeeded (was HTTP 200) |
| 1 | some products saved, some failed (was 207) |
| 2 | every product failed, or an unexpected error (was 500) |
| 3 | bad command line |

Unexpected errors are logged to `usp_APIExceptionLog` and print only
`{ "errors": { "Error": ["Something went wrong. Please try again."] } }`.

## Scheduling
Publish (`dotnet publish -c Release`) and copy the output folder to the server (needs the .NET 8 runtime).
In Task Scheduler create a task with Action "Start a program" → `FynityConsoleDemo.exe`.
Give the account the task runs as **Modify** permission on the output folder, and make sure the server can reach
`uatapi-ipbb.fynity.in` over HTTPS. Set `ConnectionStrings:Default` to log errors to the database.
