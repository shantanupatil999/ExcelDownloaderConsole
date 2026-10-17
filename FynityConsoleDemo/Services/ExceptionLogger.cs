using System.Data;
using Microsoft.Data.SqlClient;

namespace FynityConsoleDemo.Services;

/// <summary>
/// Writes exception details to usp_APIExceptionLog, exactly as given:
/// same stored procedure name and parameters. Two additions on top of the
/// original:
///   1. If ConnectionStrings:Default is empty (e.g. no DB wired up yet in
///      this environment), it falls back to the normal ASP.NET Core logger
///      instead of throwing — so a missing DB never breaks the app.
///   2. The DB call itself is wrapped in try/catch so a logging failure
///      (DB down, bad connection string, proc missing, etc.) can never mask
///      or throw over the original exception the caller is trying to log.
/// </summary>
public class ExceptionLogger : IExceptionLogger
{
    private readonly string _connectionString;
    private readonly ILogger<ExceptionLogger> _logger;

    public ExceptionLogger(IConfiguration configuration, ILogger<ExceptionLogger> logger)
    {
        _connectionString = configuration.GetConnectionString("Default") ?? string.Empty;
        _logger = logger;
    }

    public async Task LogException(string controllerName, string actionName, Exception ex, string remark, string tokenId = "")
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _logger.LogError(ex,
                "ConnectionStrings:Default is not configured — logging to usp_APIExceptionLog skipped. " +
                "[{Controller}.{Action}] {Remark}", controllerName, actionName, remark);
            return;
        }

        try
        {
            using var sqlConnection = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand("usp_APIExceptionLog", sqlConnection)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.Add(new SqlParameter("@ControllerName", controllerName));
            cmd.Parameters.Add(new SqlParameter("@ActionName", actionName));
            cmd.Parameters.Add(new SqlParameter("@ErrorMessage", ex.Message));
            cmd.Parameters.Add(new SqlParameter("@ErrorStackTrace", ex.StackTrace ?? string.Empty));
            cmd.Parameters.Add(new SqlParameter("@Remark", remark));
            cmd.Parameters.Add(new SqlParameter("@TokenId", tokenId));

            await sqlConnection.OpenAsync();
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception loggingEx)
        {
            // Never let a failure to log hide the original exception or crash the request.
            _logger.LogError(loggingEx,
                "Failed to write to usp_APIExceptionLog for {Controller}.{Action}", controllerName, actionName);
            _logger.LogError(ex,
                "Original exception for {Controller}.{Action} ({Remark})", controllerName, actionName, remark);
        }
    }
}
