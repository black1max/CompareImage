using System;
using System.Diagnostics;
using System.IO;

namespace CompareImage.Setup;

class Program
{
    static int Main(string[] args)
    {
        Console.WriteLine("==========================================================");
        Console.WriteLine(" CompareImage セットアップ・パッケージ生成ツール");
        Console.WriteLine("==========================================================");

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        // プロジェクトディレクトリの探索
        string current = AppDomain.CurrentDomain.BaseDirectory;
        string? scriptPath = null;

        for (int i = 0; i < 5; i++)
        {
            string candidate = Path.Combine(current, "BuildInstaller.ps1");
            if (File.Exists(candidate))
            {
                scriptPath = candidate;
                break;
            }
            string setupSub = Path.Combine(current, "CompareImage.Setup", "BuildInstaller.ps1");
            if (File.Exists(setupSub))
            {
                scriptPath = setupSub;
                break;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        if (scriptPath == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("エラー: BuildInstaller.ps1 が見つかりませんでした。");
            Console.ResetColor();
            return 1;
        }

        Console.WriteLine($"実行スクリプト: {scriptPath}");
        Console.WriteLine();

        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false
        };

        try
        {
            using var process = Process.Start(psi);
            process?.WaitForExit();
            return process?.ExitCode ?? 0;
        }
        catch
        {
            // pwsh がない場合は powershell.exe でフォールバック
            psi.FileName = "powershell.exe";
            using var process = Process.Start(psi);
            process?.WaitForExit();
            return process?.ExitCode ?? 0;
        }
    }
}
