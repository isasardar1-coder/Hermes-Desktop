using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace HermesDesktop.Services;

internal static class HermesUpdateService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/RedWoodOG/Hermes-Desktop/releases/latest";
    private const string PortableAssetName = "HermesDesktop-portable-x64.zip";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    internal static string CurrentVersion => GetCurrentVersion();

    internal static async Task<HermesUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var latestVersion = root.GetProperty("tag_name").GetString() ?? "unknown";
        var releaseUrl = root.GetProperty("html_url").GetString() ?? "https://github.com/RedWoodOG/Hermes-Desktop/releases";
        var publishedAt = root.TryGetProperty("published_at", out var publishedAtEl)
            ? publishedAtEl.GetString()
            : null;

        string? downloadUrl = null;
        if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsEl.EnumerateArray())
            {
                if (!string.Equals(asset.GetProperty("name").GetString(), PortableAssetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                break;
            }
        }

        var currentVersion = CurrentVersion;
        var updateAvailable = CompareVersions(currentVersion, latestVersion) < 0;
        var status = updateAvailable
            ? $"Update available: {latestVersion}"
            : $"You are up to date ({currentVersion})";

        if (!string.IsNullOrWhiteSpace(publishedAt) &&
            DateTimeOffset.TryParse(publishedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published))
        {
            status += $" • Published {published:yyyy-MM-dd}";
        }

        return new HermesUpdateInfo(
            currentVersion,
            latestVersion,
            releaseUrl,
            downloadUrl,
            status,
            updateAvailable);
    }

    internal static void OpenUpdate(HermesUpdateInfo updateInfo)
    {
        var target = !string.IsNullOrWhiteSpace(updateInfo.DownloadUrl)
            ? updateInfo.DownloadUrl
            : updateInfo.ReleaseUrl;

        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true,
        });
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HermesDesktop/1.0");
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    private static string GetCurrentVersion()
    {
        try
        {
            var version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch
        {
            var assemblyVersion = typeof(HermesUpdateService).Assembly.GetName().Version;
            if (assemblyVersion is not null)
                return assemblyVersion.ToString();

            return "unknown";
        }
    }

    private static int CompareVersions(string currentVersion, string latestVersion)
    {
        var current = ParseVersionParts(currentVersion);
        var latest = ParseVersionParts(latestVersion);
        var count = Math.Max(current.Count, latest.Count);

        for (var index = 0; index < count; index++)
        {
            var currentPart = index < current.Count ? current[index] : 0;
            var latestPart = index < latest.Count ? latest[index] : 0;
            if (currentPart != latestPart)
                return currentPart.CompareTo(latestPart);
        }

        return 0;
    }

    private static List<int> ParseVersionParts(string version)
    {
        return version
            .Trim()
            .TrimStart('v', 'V')
            .Split(new[] { '.', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0)
            .ToList();
    }
}

internal sealed record HermesUpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string? DownloadUrl,
    string Status,
    bool UpdateAvailable);
