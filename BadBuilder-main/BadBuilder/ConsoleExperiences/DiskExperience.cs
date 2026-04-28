using Spectre.Console;
using BadBuilder.Models;
using BadBuilder.Helpers;

namespace BadBuilder
{
    internal partial class Program
    {
        static string PromptDiskSelection(List<DiskInfo> disks)
        {
            // Markup.Escape prevents drive letters, volume labels, or mount paths
            // that contain '[' or ']' from being misinterpreted as Spectre markup tags.
            var choices = disks.Select(disk =>
                Markup.Escape($"{disk.DriveLetter} ({disk.SizeFormatted}) - {disk.Type}")).ToList();

            return AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a disk to format:")
                    .HighlightStyle(GreenStyle)
                    .AddChoices(choices)
            );
        }

        static bool PromptFormatConfirmation(DiskInfo disk)
        {
            // On Linux show the device node; on Windows show the drive letter
            string diskDisplay = !string.IsNullOrEmpty(disk.DevicePath)
                ? $"{disk.DevicePath} ({disk.DriveLetter.TrimEnd('/')})"
                : disk.DriveLetter.TrimEnd('\\', '/');

            return AnsiConsole.Prompt(
                new TextPrompt<bool>($"[#FF7200 bold]WARNING: [/]Are you sure you would like to format [bold]{diskDisplay}[/]? All data on this drive will be lost.")
                    .AddChoice(true)
                    .AddChoice(false)
                    .DefaultValue(false)
                    .ChoicesStyle(GreenStyle)
                    .DefaultValueStyle(OrangeStyle)
                    .WithConverter(choice => choice ? "y" : "n")
            );
        }

        static bool FormatDisk(DiskInfo disk)
        {
            bool ret = true;
            string output = string.Empty;

            AnsiConsole.Status().SpinnerStyle(LightOrangeStyle).Start($"[#76B900]Formatting disk[/] {disk.DriveLetter} ({disk.SizeFormatted}) - {disk.Type}", async ctx =>
            {
                ClearConsole();
                output = DiskHelper.FormatDisk(disk);
                if (output != string.Empty) ret = false;
            });

            if (!ret)
            {
                AnsiConsole.Clear();
                ShowWelcomeMessage();
                Console.Write("\n" + output + "\n");
            }

            return ret;
        }
    }
}