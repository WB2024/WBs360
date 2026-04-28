using BadBuilder.Models;
using BadBuilder.Formatter;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Spectre.Console;

namespace BadBuilder.Helpers
{
    internal static class DiskHelper
    {
        internal static List<DiskInfo> GetDisks()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return GetDisksLinux();

            var disks = new List<DiskInfo>();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    string driveLetter = drive.Name;
                    string volumeLabel = drive.VolumeLabel;
                    string type = drive.DriveType.ToString();
                    long totalSize = drive.TotalSize;
                    long availableFreeSpace = drive.AvailableFreeSpace;
                    int diskNumber = 2;

                    disks.Add(new DiskInfo(driveLetter, type, totalSize, volumeLabel, availableFreeSpace, diskNumber));
                }
            }

            return disks;
        }

        private static List<DiskInfo> GetDisksLinux()
        {
            var disks = new List<DiskInfo>();
            try
            {
                int exitCode = RunProcess("lsblk", "-J -b -o NAME,SIZE,TYPE,MOUNTPOINTS,LABEL,RM", out string output, out _);
                if (exitCode != 0)
                {
                    // Older lsblk versions use singular "mountpoint"
                    RunProcess("lsblk", "-J -b -o NAME,SIZE,TYPE,MOUNTPOINT,LABEL,RM", out output, out _);
                }

                using var doc = JsonDocument.Parse(output);
                var devices = doc.RootElement.GetProperty("blockdevices");

                foreach (var device in devices.EnumerateArray())
                {
                    if (!GetBoolProperty(device, "rm")) continue;

                    // Show whole-disk entries (no children) as well as partitions
                    if (device.TryGetProperty("children", out var children))
                    {
                        foreach (var child in children.EnumerateArray())
                            TryAddLinuxDisk(child, disks);
                    }
                    else
                    {
                        TryAddLinuxDisk(device, disks);
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to enumerate disks via lsblk: {Markup.Escape(ex.Message)}[/]");
            }
            return disks;
        }

        private static void TryAddLinuxDisk(JsonElement device, List<DiskInfo> disks)
        {
            string type = GetStringProperty(device, "type") ?? "";
            if (type != "part" && type != "disk") return;

            string name = GetStringProperty(device, "name") ?? "";
            long size = GetLongProperty(device, "size");
            string? label = GetStringProperty(device, "label");
            string devicePath = $"/dev/{name}";

            // lsblk >= 2.37 uses "mountpoints" (array); older uses "mountpoint" (string)
            string? mountPoint = null;
            if (device.TryGetProperty("mountpoints", out var mpts))
            {
                foreach (var mpt in mpts.EnumerateArray())
                {
                    if (mpt.ValueKind != JsonValueKind.Null)
                    {
                        mountPoint = mpt.GetString();
                        break;
                    }
                }
            }
            else if (device.TryGetProperty("mountpoint", out var mpt) && mpt.ValueKind != JsonValueKind.Null)
            {
                mountPoint = mpt.GetString();
            }

            string driveLetter = mountPoint != null
                ? (mountPoint.EndsWith('/') ? mountPoint : mountPoint + '/')
                : $"(unmounted) {devicePath}";

            disks.Add(new DiskInfo(driveLetter, "Removable", size, label ?? "", 0, 0, devicePath));
        }

        internal static string FormatDisk(DiskInfo disk)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return FormatDiskLinux(disk.DevicePath);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "\u001b[38;2;255;114;0m[-]\u001b[0m Formatting is only supported on Windows and Linux. Please format your drive manually as FAT32 and try again.";

            return DiskFormatter.FormatVolume(disk.DriveLetter[0], disk.TotalSize);
        }

        private static string FormatDiskLinux(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath))
                return "\u001b[38;2;255;114;0m[-]\u001b[0m Device path is unknown. Please format your drive manually as FAT32 and try again.";

            // Unmount first (ignore errors — drive may already be unmounted)
            RunProcess("umount", devicePath, out _, out _);

            int exitCode = RunProcess("mkfs.vfat", $"-F 32 -n BADUPDATE {devicePath}", out _, out string stderr);
            if (exitCode != 0)
                return $"\u001b[38;2;255;114;0m[-]\u001b[0m mkfs.vfat failed (exit code {exitCode}). You may need to run BadBuilder with sudo.\n{stderr.Trim()}";

            return string.Empty;
        }

        /// <summary>
        /// After formatting on Linux the drive remounts under a new label. Poll lsblk to find the new mount point.
        /// </summary>
        internal static string? FindMountPointForDeviceLinux(string devicePath)
        {
            try
            {
                string devName = Path.GetFileName(devicePath); // "sdb1"
                RunProcess("lsblk", $"-J -b -o NAME,MOUNTPOINTS {devicePath}", out string output, out _);

                // Fallback to singular if needed
                if (!output.Contains("mountpoints"))
                    RunProcess("lsblk", $"-J -b -o NAME,MOUNTPOINT {devicePath}", out output, out _);

                using var doc = JsonDocument.Parse(output);
                var devices = doc.RootElement.GetProperty("blockdevices");
                foreach (var device in devices.EnumerateArray())
                {
                    string? mp = ExtractMountPoint(device);
                    if (!string.IsNullOrEmpty(mp))
                        return mp.EndsWith('/') ? mp : mp + '/';
                }
            }
            catch { }
            return null;
        }

        private static string? ExtractMountPoint(JsonElement device)
        {
            if (device.TryGetProperty("mountpoints", out var mpts))
            {
                foreach (var mpt in mpts.EnumerateArray())
                {
                    if (mpt.ValueKind != JsonValueKind.Null)
                    {
                        var mp = mpt.GetString();
                        if (!string.IsNullOrEmpty(mp)) return mp;
                    }
                }
            }
            else if (device.TryGetProperty("mountpoint", out var mpt) && mpt.ValueKind != JsonValueKind.Null)
            {
                return mpt.GetString();
            }
            return null;
        }

        private static string? GetStringProperty(JsonElement el, string name) =>
            el.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null
                ? prop.GetString()
                : null;

        private static long GetLongProperty(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null) return 0;
            if (prop.ValueKind == JsonValueKind.Number) return prop.GetInt64();
            if (prop.ValueKind == JsonValueKind.String && long.TryParse(prop.GetString(), out long val)) return val;
            return 0;
        }

        private static bool GetBoolProperty(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var prop)) return false;
            return prop.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => prop.GetInt32() == 1,
                JsonValueKind.String => prop.GetString() == "1",
                _ => false
            };
        }

        internal static int RunProcess(string fileName, string args, out string stdout, out string stderr)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            stdout = process.StandardOutput.ReadToEnd();
            stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode;
        }
    }
}