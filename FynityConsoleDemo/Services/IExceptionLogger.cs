namespace FynityConsoleDemo.Services;

public interface IExceptionLogger
{
    Task LogException(string controllerName, string actionName, Exception ex, string remark, string tokenId = "");
}
