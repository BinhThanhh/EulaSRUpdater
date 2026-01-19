using System.Diagnostics;
using EulaSR;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var logger = Logger.ConsoleOnly();
var updater = new GameUpdater(logger);

Console.WriteLine("╔═════════════════════════════════════════════╗");
Console.WriteLine("║           EULA SR Game Updater              ║");
Console.WriteLine("║        Honkai: Star Rail Update Tool        ║");
Console.WriteLine("╚═════════════════════════════════════════════╝");
Console.WriteLine();

try
{
    Console.WriteLine("=== Game Update ===");
    Console.WriteLine();

    Console.Write("Enter game folder path: ");
    var gamePath = Console.ReadLine()?.Trim() ?? "";

    if (string.IsNullOrWhiteSpace(gamePath))
    {
        Console.WriteLine("❌ Game path cannot be empty.");
        return;
    }

    if (!Directory.Exists(gamePath))
    {
        Console.WriteLine($"❌ Game folder does not exist: {gamePath}");
        return;
    }

    Console.Write("Enter hdiff file path (.7z): ");
    var hdiffPath = Console.ReadLine()?.Trim() ?? "";

    if (string.IsNullOrWhiteSpace(hdiffPath))
    {
        Console.WriteLine("❌ HDiff file path cannot be empty.");
        return;
    }

    if (!File.Exists(hdiffPath))
    {
        Console.WriteLine($"❌ HDiff file does not exist: {hdiffPath}");
        return;
    }

    Console.Write("Does the 7z file have a password? (y/N): ");
    var hasPasswordInput = Console.ReadLine()?.Trim().ToLower();

    string? password = null;

    if (hasPasswordInput == "y" || hasPasswordInput == "yes")
    {
        password = ReadPassword("Enter password: ");

        if (string.IsNullOrEmpty(password))
        {
            Console.WriteLine("❌ Update cancelled - no password provided.");
            return;
        }

        // Verify password
        Console.WriteLine("🔍 Verifying password...");
        var isValidPassword = await VerifyPassword(hdiffPath, password);

        if (!isValidPassword)
        {
            Console.WriteLine("❌ Invalid password or cannot verify. Update cancelled.");
            return;
        }

        Console.WriteLine("✅ Password is valid!");
    }
    Console.WriteLine();

    // Perform update
    await updater.UpdateGameAsync(gamePath, hdiffPath, password);

    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Unexpected error: {ex.Message}");
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
}

static string ReadPassword(string prompt = "Enter password: ")
{
    Console.Write(prompt);
    var password = new List<char>();
    ConsoleKeyInfo key;

    do
    {
        key = Console.ReadKey(true);

        if (key.Key == ConsoleKey.Backspace && password.Count > 0)
        {
            password.RemoveAt(password.Count - 1);
            Console.Write("\b \b");
        }
        else if (key.Key != ConsoleKey.Enter && key.Key != ConsoleKey.Backspace)
        {
            password.Add(key.KeyChar);
            Console.Write("*");
        }
    } while (key.Key != ConsoleKey.Enter);

    Console.WriteLine();
    return new string(password.ToArray());
}


static async Task<bool> VerifyPassword(string archivePath, string password)
{
    try
    {
        var sevenZipPath = Find7ZipExecutable();

        var startInfo = new ProcessStartInfo
        {
            FileName = sevenZipPath,
            Arguments = $"t \"{archivePath}\" -p\"{password}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return process.ExitCode == 0 &&
               !error.Contains("Wrong password") &&
               !error.Contains("Data error") &&
               !error.Contains("Cannot open encrypted archive");
    }
    catch (Exception)
    {
        return false;
    }
}

static string Find7ZipExecutable()
{
    var possibleNames = new[] { "7z.exe", "7za.exe", "7z" };
    var possiblePaths = new[]
    {
        @"C:\Program Files\7-Zip\7z.exe",
        @"C:\Program Files (x86)\7-Zip\7z.exe"
    };

    // Check default paths
    foreach (var path in possiblePaths)
    {
        if (File.Exists(path))
        {
            return path;
        }
    }

    // Search in PATH
    var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
    var paths = pathEnv.Split(Path.PathSeparator);

    foreach (var path in paths)
    {
        foreach (var name in possibleNames)
        {
            var fullPath = Path.Combine(path, name);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }
    }

    foreach (var name in possibleNames)
    {
        if (File.Exists(name))
        {
            return Path.GetFullPath(name);
        }
    }

    throw new FileNotFoundException("7-Zip executable not found. Please install 7-Zip and ensure it is in PATH.");
}