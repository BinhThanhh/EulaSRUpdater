using System.Diagnostics;
using EulaSR;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Kiểm tra argument để enable file logging nếu cần
var enableFileLogging = args.Contains("--log-file") || args.Contains("-l");
var logger = enableFileLogging ? Logger.WithFile() : Logger.ConsoleOnly();
var updater = new GameUpdater(logger);

if (enableFileLogging)
{
    Console.WriteLine($"📝 File logging enabled: {logger.GetLogFilePath()}");
}

// Hiển thị help nếu có --help
if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("Usage: EulaSR.exe [options]");
    Console.WriteLine("Options:");
    Console.WriteLine("  --log-file, -l    Enable file logging (default: console only)");
    Console.WriteLine("  --help, -h        Show this help message");
    Console.WriteLine();
    return;
}

Console.WriteLine("╔═════════════════════════════════════════════╗");
Console.WriteLine("║           EULA SR Game Updater              ║");
Console.WriteLine("║        Công cụ cập nhật Honkai: Star Rail   ║");
Console.WriteLine("╚═════════════════════════════════════════════╝");
Console.WriteLine();

try
{
    while (true)
    {
        Console.WriteLine("Chọn một tùy chọn:");
        Console.WriteLine("1. Cập nhật game (một hoặc nhiều file hdiff)");
        Console.WriteLine("2. Cập nhật game từ file update");
        Console.WriteLine("3. Hiển thị thông tin game");
        Console.WriteLine("4. Thoát");
        Console.WriteLine();
        Console.Write("Nhập lựa chọn (1-4): ");

        var choice = Console.ReadLine()?.Trim();

        switch (choice)
        {
            case "1":
                await UpdateWithHdiffFiles();
                break;
            case "2":
                await UpdateFromUpdateFile();
                break;
            case "3":
                await ShowGameInfo();
                break;
            case "4":
                Console.WriteLine("Tạm biệt!");
                return;
            default:
                Console.WriteLine("❌ Lựa chọn không hợp lệ. Vui lòng chọn từ 1-4.");
                break;
        }

        if (choice != "4")
        {
            Console.WriteLine();
            Console.WriteLine("Nhấn phím bất kỳ để tiếp tục...");
            Console.ReadKey();
            Console.Clear();
            Console.WriteLine("╔═════════════════════════════════════════════╗");
            Console.WriteLine("║           EULA SR Game Updater              ║");
            Console.WriteLine("║        Công cụ cập nhật Honkai: Star Rail   ║");
            Console.WriteLine("╚═════════════════════════════════════════════╝");
            Console.WriteLine();
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Lỗi không mong muốn: {ex.Message}");
    Console.WriteLine("Nhấn phím bất kỳ để thoát...");
    Console.ReadKey();
}

async Task UpdateWithHdiffFiles()
{
    Console.WriteLine();
    Console.WriteLine("=== Cập nhật game ===");
    
    Console.Write("Nhập đường dẫn thư mục game: ");
    var gamePath = Console.ReadLine()?.Trim() ?? "";
    
    if (string.IsNullOrWhiteSpace(gamePath))
    {
        Console.WriteLine("❌ Đường dẫn game không được để trống.");
        return;
    }
    
    var hdiffPaths = new List<string>();
    
    Console.WriteLine();
    Console.WriteLine("Nhập đường dẫn các file hdiff (.7z):");
    Console.WriteLine("- Nhập từng file một, nhấn Enter sau mỗi file");
    Console.WriteLine("- Nhấn Enter trống để kết thúc nhập");
    Console.WriteLine();
    
    int index = 1;
    while (true)
    {
        if (index == 1)
        {
            Console.Write("File hdiff: ");
        }
        else
        {
            Console.Write($"File hdiff {index} (Enter để kết thúc): ");
        }
        
        var hdiffPath = Console.ReadLine()?.Trim();
        
        if (string.IsNullOrWhiteSpace(hdiffPath))
        {
            if (hdiffPaths.Count == 0)
            {
                Console.WriteLine("❌ Phải nhập ít nhất 1 file hdiff.");
                continue;
            }
            break;
        }
        
        if (!File.Exists(hdiffPath))
        {
            Console.WriteLine($"❌ File không tồn tại: {hdiffPath}");
            continue;
        }
        
        hdiffPaths.Add(hdiffPath);
        Console.WriteLine($"✅ Đã thêm: {Path.GetFileName(hdiffPath)}");
        index++;
    }
    
    Console.WriteLine();
    Console.WriteLine("=== Thông tin cập nhật ===");
    Console.WriteLine($"Game Path: {gamePath}");
    Console.WriteLine($"Số file hdiff: {hdiffPaths.Count}");
    for (int i = 0; i < hdiffPaths.Count; i++)
    {
        Console.WriteLine($"  {i + 1}. {Path.GetFileName(hdiffPaths[i])}");
    }
    Console.WriteLine();
    
    Console.Write("Bạn có muốn tiếp tục? (y/N): ");
    var confirm = Console.ReadLine()?.Trim().ToLower();
    
    if (confirm == "n" || confirm == "no")
    {
        Console.WriteLine("Đã hủy cập nhật.");
        return;
    }
    
    // Thực hiện cập nhật cho tất cả trường hợp khác (y, yes hoặc default)
    Console.WriteLine();
    
    // Hỏi người dùng về password cho file 7z
    Console.WriteLine("🔐 Các file 7z có thể được bảo vệ bằng password.");
    Console.Write("File 7z có password không? (y/N): ");
    var hasPasswordInput = Console.ReadLine()?.Trim().ToLower();
    
    string? password = null;
    bool allFilesHandled = true;
    
    if (hasPasswordInput == "y" || hasPasswordInput == "yes")
    {
        password = ReadPassword("Nhập password cho file 7z: ");
        
        if (string.IsNullOrEmpty(password))
        {
            Console.WriteLine("❌ Đã hủy cập nhật do không nhập password.");
            allFilesHandled = false;
        }
        else
        {
            // Xác minh password với file đầu tiên
            Console.WriteLine("🔍 Đang xác minh password...");
            var isValidPassword = await VerifyPassword(hdiffPaths[0], password);
            
            if (!isValidPassword)
            {
                Console.WriteLine("❌ Password không đúng hoặc không thể xác minh. Đã hủy cập nhật.");
                allFilesHandled = false;
            }
            else
            {
                Console.WriteLine("✅ Password hợp lệ!");
            }
        }
    }
    
    if (allFilesHandled)
    {
        // Tạo config với password nếu có
        var config = new GameUpdater.UpdateConfig
        {
            GamePath = gamePath,
            HDiffPaths = hdiffPaths.ToArray(),
            Password = password
        };
        
        // Tự động phát hiện version từ tên file đầu tiên
        var firstHdiffFileName = Path.GetFileNameWithoutExtension(hdiffPaths[0]);
        var versionInfo = ExtractVersionFromFileName(firstHdiffFileName);
        config.CurrentVersion = versionInfo.currentVersion;
        config.TargetVersion = versionInfo.targetVersion;
        
        await updater.UpdateGameAsync(config);
    }
    else
    {
        Console.WriteLine("Đã hủy cập nhật do không thể xử lý file được mã hóa.");
    }
}

async Task UpdateFromUpdateFile()
{
    Console.WriteLine();
    Console.WriteLine("=== Cập nhật từ file update ===");
    
    Console.Write("Nhập đường dẫn thư mục game: ");
    var gamePath = Console.ReadLine()?.Trim() ?? "";
    
    if (string.IsNullOrWhiteSpace(gamePath))
    {
        Console.WriteLine("❌ Đường dẫn game không được để trống.");
        return;
    }
    
    Console.Write("Nhập đường dẫn file update (.txt): ");
    var updateFilePath = Console.ReadLine()?.Trim() ?? "";
    
    if (string.IsNullOrWhiteSpace(updateFilePath))
    {
        Console.WriteLine("❌ Đường dẫn file update không được để trống.");
        return;
    }
    
    if (!File.Exists(updateFilePath))
    {
        Console.WriteLine($"❌ File update không tồn tại: {updateFilePath}");
        return;
    }
    
    Console.WriteLine();
    Console.WriteLine($"Game Path: {gamePath}");
    Console.WriteLine($"Update File: {updateFilePath}");
    Console.WriteLine();
    
    Console.Write("Bạn có muốn tiếp tục? (y/N): ");
    var confirm = Console.ReadLine()?.Trim().ToLower();
    
    if (confirm == "y" || confirm == "yes")
    {
        Console.WriteLine();
        
        // Đọc file update để lấy danh sách file hdiff
        try
        {
            var updateContent = await File.ReadAllTextAsync(updateFilePath);
            var hdiffPaths = new List<string>();

            var lines = updateContent.Split('\n');
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (!string.IsNullOrEmpty(trimmedLine) && !trimmedLine.StartsWith("#"))
                {
                    if (File.Exists(trimmedLine))
                    {
                        hdiffPaths.Add(trimmedLine);
                    }
                }
            }

            if (hdiffPaths.Count == 0)
            {
                Console.WriteLine("❌ Không tìm thấy file hdiff nào trong file update");
                return;
            }

            // Hỏi người dùng về password cho file 7z
            Console.WriteLine("🔐 Các file 7z có thể được bảo vệ bằng password.");
            Console.Write("File 7z có password không? (y/N): ");
            var hasPasswordInput = Console.ReadLine()?.Trim().ToLower();
            
            string? password = null;
            bool allFilesHandled = true;
            
            if (hasPasswordInput == "y" || hasPasswordInput == "yes")
            {
                password = ReadPassword("Nhập password cho file 7z: ");
                
                if (string.IsNullOrEmpty(password))
                {
                    Console.WriteLine("❌ Đã hủy cập nhật do không nhập password.");
                    allFilesHandled = false;
                }
                else if (hdiffPaths.Count > 0)
                {
                    // Xác minh password với file đầu tiên
                    Console.WriteLine("🔍 Đang xác minh password...");
                    var isValidPassword = await VerifyPassword(hdiffPaths[0], password);
                    
                    if (!isValidPassword)
                    {
                        Console.WriteLine("❌ Password không đúng hoặc không thể xác minh. Đã hủy cập nhật.");
                        allFilesHandled = false;
                    }
                    else
                    {
                        Console.WriteLine("✅ Password hợp lệ!");
                    }
                }
            }
            
            if (allFilesHandled)
            {
                // Tạo config với password nếu có
                var config = new GameUpdater.UpdateConfig
                {
                    GamePath = gamePath,
                    HDiffPaths = hdiffPaths.ToArray(),
                    UpdateFilePath = updateFilePath,
                    CurrentVersion = "unknown",
                    TargetVersion = "unknown",
                    Password = password
                };
                
                await updater.UpdateGameAsync(config);
            }
            else
            {
                Console.WriteLine("Đã hủy cập nhật do không thể xử lý file được mã hóa.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Lỗi khi đọc file update: {ex.Message}");
        }
    }
    else
    {
        Console.WriteLine("Đã hủy cập nhật.");
    }
}

async Task ShowGameInfo()
{
    Console.WriteLine();
    Console.Write("Nhập đường dẫn thư mục game (Enter để dùng mặc định F:\\StarRail): ");
    var gamePath = Console.ReadLine()?.Trim();
    
    if (string.IsNullOrWhiteSpace(gamePath))
    {
        gamePath = @"F:\StarRail";
    }
    
    Console.WriteLine();
    await updater.ShowGameInfoAsync(gamePath);
}

/// <summary>
/// Nhập password một cách an toàn (ẩn ký tự)
/// </summary>
static string ReadPassword(string prompt = "Nhập password: ")
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

/// <summary>
/// Xác minh password cho file 7z
/// </summary>
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

        // Nếu test thành công và không có lỗi về password
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



/// <summary>
/// Tìm 7-Zip executable
/// </summary>
static string Find7ZipExecutable()
{
    var possibleNames = new[] { "7z.exe", "7za.exe", "7z" };
    var possiblePaths = new[]
    {
        @"C:\Program Files\7-Zip\7z.exe",
        @"C:\Program Files (x86)\7-Zip\7z.exe"
    };

    // Kiểm tra đường dẫn mặc định
    foreach (var path in possiblePaths)
    {
        if (File.Exists(path))
        {
            return path;
        }
    }

    // Tìm trong PATH
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

    // Tìm trong thư mục hiện tại
    foreach (var name in possibleNames)
    {
        if (File.Exists(name))
        {
            return Path.GetFullPath(name);
        }
    }

    throw new FileNotFoundException("Không tìm thấy 7-Zip executable. Vui lòng cài đặt 7-Zip và đảm bảo nó có trong PATH.");
}

/// <summary>
/// Trích xuất thông tin version từ tên file
/// </summary>
static (string currentVersion, string targetVersion) ExtractVersionFromFileName(string fileName)
{
    // Ví dụ: game_3.5.51_3.5.52_hdiff -> (3.5.51, 3.5.52)
    var parts = fileName.Split('_');
    
    // Kiểm tra format StarRail_x.x.x_x.x.x_hdiff_seg
    if (parts.Length >= 4 && parts[0].Equals("StarRail", StringComparison.OrdinalIgnoreCase))
    {
        return (parts[1], parts[2]);
    }
    // Kiểm tra format cũ game_x.x.x_x.x.x_hdiff
    else if (parts.Length >= 3)
    {
        return (parts[1], parts[2]);
    }

    // Fallback
    return ("unknown", "unknown");
}