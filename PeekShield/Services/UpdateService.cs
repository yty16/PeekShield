using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PeekShield.Models;

namespace PeekShield.Services;

public class UpdateInfo
{
    public string? Version { get; set; }
    public string? TagName { get; set; }
    public string? Name { get; set; }
    public string Changelog { get; set; } = "";
    public string ReleaseUrl { get; set; } = BuildConstants.GitHubReleasesUrl;
    public List<UpdateAsset> Assets { get; set; } = new();
    public bool HasUpdate { get; set; }
    public string? Error { get; set; }
}

public class UpdateAsset
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public long Size { get; set; }
}

public static class UpdateService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25) };

    static UpdateService()
    {
        try { _http.DefaultRequestHeaders.UserAgent.ParseAdd("PeekShield-Updater"); } catch { }
    }

    public static string CurrentVersion
    {
        get
        {
            if (System.Version.TryParse(BuildConstants.Version, out var v))
                return v.ToString();
            return BuildConstants.Version;
        }
    }

    public static async Task<UpdateInfo> CheckAsync()
    {
        var info = new UpdateInfo();
        try
        {
            using var resp = await _http.GetAsync("https://api.github.com/repos/yty16/PeekShield/releases/latest");
            if (!resp.IsSuccessStatusCode)
            {
                info.Error = "无法连接更新服务器（HTTP " + (int)resp.StatusCode + "）";
                return info;
            }
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("tag_name", out var t)) info.TagName = t.GetString();
            if (root.TryGetProperty("name", out var n)) info.Name = n.GetString();
            if (root.TryGetProperty("body", out var b)) info.Changelog = b.GetString() ?? "";
            if (root.TryGetProperty("html_url", out var h)) info.ReleaseUrl = h.GetString() ?? info.ReleaseUrl;
            if (root.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in a.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var an) ? (an.GetString() ?? "") : "";
                    var url = item.TryGetProperty("browser_download_url", out var au) ? (au.GetString() ?? "") : "";
                    long size = item.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var sv) ? sv : 0;
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                        info.Assets.Add(new UpdateAsset { Name = name, Url = url, Size = size });
                }
            }
            info.Version = NormalizeVersion(info.TagName ?? info.Name);
            info.HasUpdate = IsNewer(info.Version);
        }
        catch (Exception ex)
        {
            info.Error = "检查更新失败：" + ex.Message;
        }
        return info;
    }

    private static string? NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().TrimStart('v', 'V');
        return s.Contains('.') ? s : null;
    }

    private static bool IsNewer(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote)) return false;
        try
        {
            var cur = new Version(CurrentVersion);
            var rem = new Version(remote);
            return rem > cur;
        }
        catch { return false; }
    }

    public static async Task CheckAndNotifyAsync(Window? owner)
    {
        var info = await CheckAsync();
        if (!string.IsNullOrEmpty(info.Error) || !info.HasUpdate) return;
        var s = PeekShieldEngine.Instance.Settings;
        if (s.AutoSilentUpdate)
            await PerformUpdateAsync(info, silent: true);
        else
            Dispatcher.UIThread.Post(() => ShowUpdateDialog(owner, info));
    }

    public static void ShowUpdateDialog(Window? owner, UpdateInfo info)
    {
        var w = owner ?? MainWindow.Instance;
        var dlg = new Views.UpdateDialog(info, onUpdate: () => _ = PerformUpdateAsync(info, silent: false));
        if (w != null) dlg.ShowDialog(w);
    }

    public static async Task PerformUpdateAsync(UpdateInfo info, bool silent)
    {
        try
        {
            var asset = PickAsset(info.Assets);
            if (asset == null)
            {
                Platform.OpenUrl(info.ReleaseUrl);
                return;
            }
            var tmp = Path.Combine(Path.GetTempPath(), asset.Name);
            using var resp = await _http.GetAsync(asset.Url);
            resp.EnsureSuccessStatusCode();
            await using (var fs = File.Create(tmp))
                await resp.Content.CopyToAsync(fs);
            LaunchInstaller(tmp);
        }
        catch (Exception ex)
        {
            try { LoggerService.LogInfo("自动更新失败：" + ex.Message); } catch { }
            if (!silent) Platform.OpenUrl(info.ReleaseUrl);
        }
    }

    private static UpdateAsset? PickAsset(List<UpdateAsset> assets)
    {
        if (Platform.IsWindows)
            return assets.FirstOrDefault(a => a.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (Platform.IsLinux)
            return assets.FirstOrDefault(a => a.Name.Contains("linux-x64", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".deb", StringComparison.OrdinalIgnoreCase));
        if (Platform.IsMacOS)
            return assets.FirstOrDefault(a => a.Name.Contains("osx-arm64", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".app.zip", StringComparison.OrdinalIgnoreCase));
        return null;
    }

    private static void LaunchInstaller(string path)
    {
        try
        {
            if (Platform.IsWindows)
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            else if (Platform.IsLinux)
            {
                ProcessStartInfo psi;
                if (File.Exists("/usr/bin/pkexec") || File.Exists("/bin/pkexec"))
                {
                    psi = new ProcessStartInfo("pkexec", "dpkg -i \"" + path + "\"") { UseShellExecute = false };
                }
                else
                {
                    psi = new ProcessStartInfo("xdg-open", "\"" + path + "\"") { UseShellExecute = true };
                }
                Process.Start(psi);
            }
            else if (Platform.IsMacOS)
            {
                Process.Start(new ProcessStartInfo("open", "\"" + path + "\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            try { LoggerService.LogInfo("启动安装包失败：" + ex.Message); } catch { }
            Platform.OpenUrl(BuildConstants.GitHubReleasesUrl);
            return;
        }
        App.RequestExit();
    }
}
