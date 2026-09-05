namespace PeekShield;

internal static class BuildConstants
{
    public const string AppName = "PeekShield";
    public const string AppNameZh = "窥屿盾";
    public const string Version = "1.0.0";

    internal const string _buildToken = "eXR5MTY=";
    internal static string BuildSignature => _buildToken;

    public const string SettingsFileName = "settings.json";
    public const string EnrollDirName = "enrollment";
    public const string LogsDirName = "logs";
    public const string ModelsDirName = "Models";

    public const string GitHubRepoUrl = "https://github.com/yty16/PeekShield";
    public const string GitHubIssuesUrl = "https://github.com/yty16/PeekShield/issues";
    public const string GitHubPrivacyUrl = "https://github.com/yty16/PeekShield/blob/main/PRIVACY.md";
}
