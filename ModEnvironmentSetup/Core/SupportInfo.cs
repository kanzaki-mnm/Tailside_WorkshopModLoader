namespace ModEnvironmentSetup.Core;

public static class SupportInfo
{
    // Set this once the public Releases/help page exists. Never open a placeholder URL.
    public const string ReleasePageUrl = "";
    public static string GuideUrl { get; set; } = ReleasePageUrl;
    public static bool HasGuideUrl => Uri.TryCreate(GuideUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps;
    public static string GuideButton => HasGuideUrl ? "Installation & help" : "Help (not configured)";
    public static string GuideNotice => HasGuideUrl
        ? "Opens the installation and help page in your browser."
        : "The Releases/help URL has not been configured in this preview. See the included README for installation and recovery instructions.";
    public static string ErrorHint => "Save a local report when asking for help. " + GuideNotice;
    public static string FatalReport(string app, string version, Exception error) =>
        $"{app} {version}\nTime: {DateTimeOffset.Now:O}\nOS: {Environment.OSVersion}\n" +
        $"Guide: {GuideUrl}\n\n{error}\n\nThis report was saved locally. Nothing was uploaded.\n";
}

