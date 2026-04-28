using Spectre.Console;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BadBuilder.Helpers
{
    internal static class PatchHelper
    {
        internal static async Task PatchXexAsync(string xexPath, string xexToolPath)
        {
            // On Linux, XexTool is a Windows binary — try Wine if available.
            string fileName = xexToolPath;
            string arguments = $"-m r -r a \"{xexPath}\"";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                fileName = "wine";
                arguments = $"\"{xexToolPath}\" -m r -r a \"{xexPath}\"";
            }

            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                string status = "[-]";
                string hint = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? " (ensure Wine is installed: sudo apt install wine)" : "";
                AnsiConsole.MarkupLineInterpolated($"\n[#FF7200]{status}[/] The program {Path.GetFileNameWithoutExtension(xexPath)} was unable to be patched. XexTool output:{hint}");
                Console.WriteLine(process.StandardError.ReadToEnd());
            }
        }
    }
}