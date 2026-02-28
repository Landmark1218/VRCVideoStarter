using System;
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
    static string localAppPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCVideoTools");
    static string cloudflareUrl = "";

    static Process _goHttpProcess;
    static Process _cloudflaredProcess;

    [STAThread]
    static async Task Main(string[] args)
    {
        Directory.CreateDirectory(localAppPath);

        _handler = new PHANDLER_ROUTINE(HandlerRoutine);
        SetConsoleCtrlHandler(_handler, true);

        await PrepareTools();

        Console.WriteLine("動画フォルダのパスを入力してください:");
        string videoPath = Console.ReadLine()?.Replace("\"", "") ?? "";

        if (!Directory.Exists(videoPath))
        {
            Console.WriteLine("フォルダが見つかりません。");
            return;
        }

        _goHttpProcess = StartGoHttpServer(videoPath);
        _cloudflaredProcess = StartCloudflareTunnel();

        Console.WriteLine("動画リスト取得中...");
        while (string.IsNullOrEmpty(cloudflareUrl))
            await Task.Delay(500);

        ShowMenu(videoPath);
    }
    private static bool HandlerRoutine(uint type)
    {
        if (type == 2)
        {
            Cleanup();
        }
        return false;
    }

    private static void Cleanup()
    {
        try
        {
            if (_goHttpProcess != null && !_goHttpProcess.HasExited)
                _goHttpProcess.Kill();

            if (_cloudflaredProcess != null && !_cloudflaredProcess.HasExited)
                _cloudflaredProcess.Kill();
        }
        catch {}
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

    static Process StartCloudflareTunnel()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cloudflared",
                Arguments = "tunnel --url http://localhost:8003",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (string.IsNullOrEmpty(cloudflareUrl) &&
                e.Data != null &&
                e.Data.Contains("trycloudflare.com") &&
                e.Data.Contains("https://"))
            {
                var parts = e.Data.Split(' ');
                var rawUrl = parts.FirstOrDefault(p => p.Contains("https://"));

                if (rawUrl != null)
                {
                    cloudflareUrl = rawUrl.Trim().TrimEnd('/', '|', ' ');
                }
            }
        };

        process.Start();
        process.BeginErrorReadLine();
        return process;
    }

    static void ShowMenu(string path)
    {
        while (true)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Made By Landmark0920\r\nPowered By Cloudflare Tunnel client, gohttpserver");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("注意！動画の名前は全て半角英数字のみにすること！スペースもダメです。");
            Console.ResetColor();

            Console.WriteLine("--------------------------------------------------");

            var files = Directory.GetFiles(path, "*.mp4");
            for (int i = 0; i < files.Length; i++)
            {
                Console.WriteLine($"{i + 1}: {Path.GetFileName(files[i])}");
            }

            Console.WriteLine("\n番号を選んでURLをコピー:");
            var input = Console.ReadLine();

            if (input?.ToLower() == "q")
                break;

            if (int.TryParse(input, out int index) &&
                index > 0 &&
                index <= files.Length)
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
        string goExePath = Path.Combine(localAppPath, "gohttpserver.exe");

        if (!File.Exists(goExePath))
        {
            using var client = new HttpClient();
            var zipPath = Path.Combine(localAppPath, "temp.zip");

            var bytes = await client.GetByteArrayAsync(goHttpUrl);
            await File.WriteAllBytesAsync(zipPath, bytes);

            ZipFile.ExtractToDirectory(zipPath, localAppPath, true);
            File.Delete(zipPath);
        }

        if (!CheckCloudflaredInstalled())
        {
            using var client = new HttpClient();
            var msiPath = Path.Combine(localAppPath, "setup.msi");

            var bytes = await client.GetByteArrayAsync(cloudflaredMsiUrl);
            await File.WriteAllBytesAsync(msiPath, bytes);

            var psi = new ProcessStartInfo("msiexec.exe", $"/i \"{msiPath}\" /qn")
            {
                Verb = "runas"
            };

            Process.Start(psi).WaitForExit();
            File.Delete(msiPath);
        }
    }

    static bool CheckCloudflaredInstalled()
    {
        try
        {
            Process.Start(new ProcessStartInfo("cloudflared", "--version")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            }).WaitForExit();

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SetTextSTA(string text)
    {
        var thread = new System.Threading.Thread(() =>
        {
            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}