namespace MenuBar.Services;

public static class ActiveApplicationFormatter
{
    public static string Normalize(string? productName, string processName)
    {
        var candidate = string.IsNullOrWhiteSpace(productName) ? processName : productName.Trim();
        return candidate switch
        {
            "Microsoft® Windows® Operating System" => "Windows",
            "Windows Explorer" => "文件资源管理器",
            "ApplicationFrameHost" => "Windows 应用",
            _ => candidate
        };
    }
}
