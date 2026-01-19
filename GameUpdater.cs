using System.Diagnostics;
using System.Text.Json;

namespace EulaSR;

public class GameUpdater
{
    private readonly HDiffPatcher _patcher;
    private readonly Logger _logger;

    public GameUpdater(Logger? logger = null)
    {
        _logger = logger ?? Logger.ConsoleOnly();
        _patcher = new HDiffPatcher(_logger);
    }

    public async Task<bool> UpdateGameAsync(string gamePath, string hdiffPath, string? password = null)
    {
        try
        {
            if (!ValidateUpdate(gamePath, hdiffPath))
            {
                return false;
            }
            var progress = new Progress<string>(message =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            });

            var result = await _patcher.ApplyPatchAsync(gamePath, hdiffPath, progress, password);

            if (result)
            {
                Console.WriteLine();
                Console.WriteLine("✅ Update successful!");
                Console.WriteLine($"Game has been updated.");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("❌ Update failed!");
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error during update: {ex.Message}");
            Console.WriteLine($"Details: {ex}");
            return false;
        }
    }

    private bool ValidateUpdate(string gamePath, string hdiffPath)
    {
        Console.WriteLine("Checking update requirements...");

        // Check game folder
        if (!Directory.Exists(gamePath))
        {
            Console.WriteLine($"❌ Game folder does not exist: {gamePath}");
            return false;
        }

        // Check hdiff file
        if (!File.Exists(hdiffPath))
        {
            Console.WriteLine($"❌ HDiff file does not exist: {hdiffPath}");
            return false;
        }

        // Check disk space
        var driveInfo = new DriveInfo(Path.GetPathRoot(gamePath)!);
        var hdiffSize = new FileInfo(hdiffPath).Length;
        var requiredSpace = hdiffSize * 2; // Reserve double

        if (driveInfo.AvailableFreeSpace < requiredSpace)
        {
            Console.WriteLine($"❌ Insufficient disk space. Need at least {FormatBytes(requiredSpace)}");
            Console.WriteLine($"   Available space: {FormatBytes(driveInfo.AvailableFreeSpace)}");
            return false;
        }

        // Check write permission
        try
        {
            var testFile = Path.Combine(gamePath, "write_test.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
        }
        catch
        {
            Console.WriteLine($"❌ No write permission for game folder: {gamePath}");
            Console.WriteLine("   Please run as Administrator");
            return false;
        }

        Console.WriteLine("✅ All requirements met");
        return true;
    }

    private string FormatBytes(long bytes)
    {
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:F2} {units[unitIndex]}";
    }
}
