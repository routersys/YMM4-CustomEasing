using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

public static class Updater
{
    private const string Owner = "routersys";
    private const string Repo = "YMM4-CustomEasing";

    public static async Task Main(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out int processId))
        {
            Console.WriteLine("引数が必要です: <ProcessId> <PluginDirectory>");
            return;
        }
        string pluginDirectory = args[1];

        try
        {
            Process ymmProcess = Process.GetProcessById(processId);
            Console.WriteLine($"YMM4 (PID: {processId}) の終了を待っています...");
            await ymmProcess.WaitForExitAsync();
        }
        catch (ArgumentException)
        {
            Console.WriteLine("YMM4プロセスが見つかりません。アップデートチェックを続行します...");
        }

        Console.WriteLine("YMM4が終了しました。アップデートを確認しています...");

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "YMM4-CustomEasing-Updater");

            var latestRelease = await client.GetFromJsonAsync<GitHubRelease>($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            if (latestRelease?.Assets == null || latestRelease.Assets.Length == 0)
            {
                Console.WriteLine("最新のリリースにアセットが見つかりません。");
                return;
            }

            var latestVersion = new Version(latestRelease.TagName.TrimStart('v'));

            var pluginDllPath = Path.Combine(pluginDirectory, "ymm-plugin.dll");

            var currentVersion = Assembly.LoadFrom(pluginDllPath).GetName().Version ?? new Version("0.0.0");

            Console.WriteLine($"現在のバージョン: {currentVersion}, 最新バージョン: {latestVersion}");

            if (latestVersion <= currentVersion)
            {
                Console.WriteLine("最新版を使用しています。");
                return;
            }

            Console.WriteLine("新しいバージョンが見つかりました。アップデートを開始します...");

            var asset = latestRelease.Assets[0];
            var downloadUrl = asset.BrowserDownloadUrl;
            string tempZipPath = Path.GetTempFileName();
            string tempExtractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempExtractPath);

            Console.WriteLine($"{downloadUrl} からダウンロードしています...");
            var zipBytes = await client.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(tempZipPath, zipBytes);

            Console.WriteLine("アップデートファイルを展開しています...");
            ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath);

            Console.WriteLine("プラグインファイルを置き換えています...");
            foreach (var file in Directory.GetFiles(tempExtractPath))
            {
                string destinationFile = Path.Combine(pluginDirectory, Path.GetFileName(file));
                File.Copy(file, destinationFile, true);
                Console.WriteLine($"  - 更新: {Path.GetFileName(file)}");
            }

            Console.WriteLine("一時ファイルを削除しています...");
            File.Delete(tempZipPath);
            Directory.Delete(tempExtractPath, true);

            Console.WriteLine("アップデートが完了しました！");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"エラーが発生しました: {ex.Message}");
            File.WriteAllText(Path.Combine(pluginDirectory, "update_error.log"), ex.ToString());
        }
    }
}

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("assets")]
    public Asset[] Assets { get; set; } = [];
}

public class Asset
{
    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";
}