namespace FynityConsoleDemo.Models;

/// <summary>Bound from the "Download" section of appsettings.json.</summary>
public class DownloadSettings
{
    /// <summary>
    /// Folder the xlsx files are written to. Absolute (e.g. "C:\\FynityDownloads") or
    /// relative to the project's content root (e.g. "Downloads").
    /// </summary>
    public string OutputFolder { get; set; } = "Downloads";

    /// <summary>Same From/To for every product.</summary>
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;

    /// <summary>One entry per product to download — add/remove here, no code change needed.</summary>
    public List<ProductConfig> Products { get; set; } = new();
}

public class ProductConfig
{
    public string Name { get; set; } = string.Empty;
    public string SourceType { get; set; } = "policy";
    public string SourceId { get; set; } = string.Empty;
}
