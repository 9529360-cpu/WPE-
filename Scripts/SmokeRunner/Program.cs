using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

class Program
{
    static int RunMain(string[] args)
    {
        try
        {
            // find main csproj by walking up from repo
            string dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string mainProj = null;
            for (int depth = 0; depth < 6 && dir != null; depth++)
            {
                var candidates = Directory.GetFiles(dir, "*币安量化机器人*.csproj", SearchOption.TopDirectoryOnly);
                if (candidates.Length > 0)
                {
                    mainProj = candidates[0];
                    break;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }

            if (mainProj == null)
            {
                Console.WriteLine("Main project file not found. Search failed.");
                return 1;
            }

            Console.WriteLine("Found main project: " + mainProj);

            string publishDir = Path.Combine(Path.GetDirectoryName(mainProj), "SmokePublish");
            if (Directory.Exists(publishDir))
            {
                Directory.Delete(publishDir, true);
            }
            Directory.CreateDirectory(publishDir);

            Console.WriteLine("Publishing main project to: " + publishDir);

            var psi = new ProcessStartInfo("dotnet", $"publish \"{mainProj}\" -c Release -o \"{publishDir}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var p = Process.Start(psi))
            {
                string outp = p.StandardOutput.ReadToEnd();
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                Console.WriteLine(outp);
                if (p.ExitCode != 0)
                {
                    Console.WriteLine("Publish failed:\n" + err);
                    return 1;
                }
            }

            var exe = Directory.GetFiles(publishDir, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
            string runCmd;
            string runArgs;
            if (!string.IsNullOrEmpty(exe))
            {
                runCmd = exe;
                runArgs = string.Empty;
            }
            else
            {
                var dll = Directory.GetFiles(publishDir, "*.dll", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (dll == null)
                {
                    Console.WriteLine("Published artifact not found in " + publishDir);
                    return 1;
                }
                runCmd = "dotnet";
                runArgs = "\"" + dll + "\"";
            }

            Console.WriteLine("Starting published app: " + runCmd + " " + runArgs);
            var startInfo = new ProcessStartInfo(runCmd, runArgs)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var proc = Process.Start(startInfo);
            if (proc == null)
            {
                Console.WriteLine("Failed to start published app");
                return 1;
            }

            Console.WriteLine("Smoke running for 60s...");
            Task.Delay(TimeSpan.FromSeconds(60)).Wait();

            Console.WriteLine("Stopping published app...");
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(true);
                    proc.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to stop process: " + ex.Message);
            }

            Console.WriteLine("Smoke test completed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Smoke runner failed: " + ex);
            return 1;
        }
    }
}
