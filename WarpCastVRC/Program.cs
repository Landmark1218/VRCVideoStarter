using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

class VRCVideoStarter
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate bool PHANDLER_ROUTINE(uint CtrlType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetConsoleCtrlHandler(PHANDLER_ROUTINE HandlerRoutine, bool Add);
    private static PHANDLER_ROUTINE _handler;

    static string goHttpUrl = "https://github.com/codeskyblue/gohttpserver/releases/download/1.3.0/gohttpserver_1.3.0_windows_amd64.zip";
    static string cloudflaredMsiUrl = "https://sourceforge.net/projects/cloudflare-tunnel.mirror/files/2025.11.1/cloudflared-windows-amd64.msi/download";
    static string mediaMtxUrl = "https://github.com/bluenviron/mediamtx/releases/download/v1.9.0/mediamtx_v1.9.0_windows_amd64.zip";

    static string localAppPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCVideoTools");
    static string cloudflareUrl = "";

    static Process _goHttpProcess;
    static Process _cloudflaredProcess;
    static Process _mediaMtxProcess;

    [STAThread]
    static async Task Main(string[] args)
    {
        Console.Title = "WarpCastVRC | Multi-Mode Edition";
        Directory.CreateDirectory(localAppPath);

        _handler = new PHANDLER_ROUTINE(HandlerRoutine);
        SetConsoleCtrlHandler(_handler, true);

        await PrepareTools();

        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== WarpCastVRC モード選択 ===");
        Console.ResetColor();
        Console.WriteLine("1: 動画ファイル配信モード (Local Video)");
        Console.WriteLine("2: OBSライブ配信モード (Live Stream)");
        Console.Write("\n選択してください (1 or 2): ");

        string mode = Console.ReadLine();

        if (mode == "1") await RunVideoMode();
        else if (mode == "2") await RunLiveMode();
    }

    static async Task RunVideoMode()
    {
        Console.WriteLine("\n動画フォルダのパスを入力してください:");
        string videoPath = Console.ReadLine()?.Replace("\"", "") ?? "";

        if (!Directory.Exists(videoPath)) { Console.WriteLine("フォルダが見つかりません。"); return; }

        _goHttpProcess = StartGoHttpServer(videoPath);
        _cloudflaredProcess = StartCloudflareTunnel(8003);

        Console.WriteLine("URL取得中...");
        while (string.IsNullOrEmpty(cloudflareUrl)) await Task.Delay(500);

        ShowVideoMenu(videoPath);
    }

    static async Task RunLiveMode()
    {
        _mediaMtxProcess = StartMediaMtx();
        _cloudflaredProcess = StartCloudflareTunnel(8888);

        Console.WriteLine("\n[Live Mode] 配信環境を構築中...");
        while (string.IsNullOrEmpty(cloudflareUrl)) await Task.Delay(500);

        string hlsUrl = $"{cloudflareUrl}/live/mystream/index.m3u8";
        SetTextSTA(hlsUrl);

        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("=== ライブ配信準備完了！ ===");
        Console.ResetColor();
        Console.WriteLine($"\n【OBSの設定】");
        Console.WriteLine($"サーバー: rtmp://localhost/live");
        Console.WriteLine($"ストリームキー: mystream");
        Console.WriteLine("\n--------------------------------------------------");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("【VRChat用URL (コピー済み)】");
        Console.WriteLine(hlsUrl);
        Console.ResetColor();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine("\nOBSで配信を開始すると、VRChatで視聴可能になります。");
        while (true) await Task.Delay(1000);
    }

    private static bool HandlerRoutine(uint type)
    {
        if (type == 2) Cleanup();
        return false;
    }

    private static void Cleanup()
    {
        try
        {
            if (_goHttpProcess != null && !_goHttpProcess.HasExited) _goHttpProcess.Kill();
            if (_cloudflaredProcess != null && !_cloudflaredProcess.HasExited) _cloudflaredProcess.Kill();
            if (_mediaMtxProcess != null && !_mediaMtxProcess.HasExited) _mediaMtxProcess.Kill();
        }
        catch { }
    }
    static string GetCloudflaredPath()
    {
        string prog64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "cloudflared", "cloudflared.exe");
        string prog86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "cloudflared", "cloudflared.exe");

        if (File.Exists(prog64)) return prog64;
        if (File.Exists(prog86)) return prog86;

        return "cloudflared";
    }

    static Process StartCloudflareTunnel(int port)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = GetCloudflaredPath(),
                Arguments = $"tunnel --url http://localhost:{port}",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (string.IsNullOrEmpty(cloudflareUrl) && e.Data != null && e.Data.Contains("trycloudflare.com"))
            {
                var parts = e.Data.Split(' ');
                var rawUrl = parts.FirstOrDefault(p => p.Contains("https://"));
                if (rawUrl != null) cloudflareUrl = rawUrl.Trim().TrimEnd('/', '|', ' ');
            }
        };

        process.Start();
        process.BeginErrorReadLine();
        return process;
    }

    static Process StartGoHttpServer(string path)
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(localAppPath, "gohttpserver.exe"),
            Arguments = "--port 8003",
            WorkingDirectory = path,
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }

    static Process StartMediaMtx()
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(localAppPath, "mediamtx.exe"),
            WorkingDirectory = localAppPath,
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }

    static void ShowVideoMenu(string path)
    {
        while (true)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Made By Landmark0920 | Video Mode");
            Console.ResetColor();
            Console.WriteLine("--------------------------------------------------");
            var files = Directory.GetFiles(path, "*.mp4");
            for (int i = 0; i < files.Length; i++) Console.WriteLine($"{i + 1}: {Path.GetFileName(files[i])}");
            Console.WriteLine("\n番号を選んでコピー (Qで終了):");
            var input = Console.ReadLine();
            if (input?.ToLower() == "q") break;
            if (int.TryParse(input, out int index) && index > 0 && index <= files.Length)
            {
                string fileName = Path.GetFileName(files[index - 1]);
                string fullUrl = $"{cloudflareUrl}/{Uri.EscapeDataString(fileName)}";
                SetTextSTA(fullUrl);
                Console.WriteLine($"\nコピー完了:\n{fullUrl}\n");
                System.Threading.Thread.Sleep(800);
            }
        }
    }

    static async Task PrepareTools()
    {
        await DownloadFile(goHttpUrl, "gohttpserver.exe", true);
        await DownloadFile(mediaMtxUrl, "mediamtx.exe", true);

        if (!CheckCloudflaredInstalled())
        {
            Console.WriteLine("\n[Setup] cloudflared が見つかりません。セットアップを開始します...");
            using var client = new HttpClient();
            var msiPath = Path.Combine(localAppPath, "setup.msi");

            Console.WriteLine("MSIをダウンロード中...");
            var bytes = await client.GetByteArrayAsync(cloudflaredMsiUrl);
            await File.WriteAllBytesAsync(msiPath, bytes);

            Console.WriteLine("インストーラーを起動します。インストールを完了させてください。");
            // /qn を外してインタラクティブに起動
            var psi = new ProcessStartInfo("msiexec.exe", $"/i \"{msiPath}\"") { UseShellExecute = true, Verb = "runas" };
            var process = Process.Start(psi);
            process.WaitForExit();

            File.Delete(msiPath);
            Console.WriteLine("セットアップが完了しました。");
        }
    }

    static async Task DownloadFile(string url, string fileName, bool isZip)
    {
        string filePath = Path.Combine(localAppPath, fileName);
        if (!File.Exists(filePath))
        {
            using var client = new HttpClient();
            if (isZip)
            {
                var zipPath = Path.Combine(localAppPath, "temp.zip");
                var bytes = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(zipPath, bytes);
                ZipFile.ExtractToDirectory(zipPath, localAppPath, true);
                File.Delete(zipPath);
            }
            else
            {
                var bytes = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(filePath, bytes);
            }
        }
    }

    static bool CheckCloudflaredInstalled()
    {
        string prog64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "cloudflared", "cloudflared.exe");
        string prog86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "cloudflared", "cloudflared.exe");

        if (File.Exists(prog64) || File.Exists(prog86)) return true;

        try
        {
            var psi = new ProcessStartInfo("cloudflared", "--version") { CreateNoWindow = true, UseShellExecute = false };
            Process.Start(psi).WaitForExit();
            return true;
        }
        catch { return false; }
    }

    public static void SetTextSTA(string text)
    {
        var thread = new System.Threading.Thread(() => {
            if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}