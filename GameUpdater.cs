using System.Diagnostics;
using System.Text.Json;

namespace EulaSR;

public class GameUpdater
{
    private readonly HDiffPatcher _patcher;
    private readonly string _configFile;
    private readonly Logger _logger;

    public GameUpdater(Logger? logger = null)
    {
        _logger = logger ?? Logger.ConsoleOnly();
        _patcher = new HDiffPatcher(_logger);
        _configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
    }

    public class UpdateConfig
    {
        public string GamePath { get; set; } = "";
        public string[] HDiffPaths { get; set; } = Array.Empty<string>();
        public string CurrentVersion { get; set; } = "";
        public string TargetVersion { get; set; } = "";
        public string UpdateFilePath { get; set; } = "";
        public string? Password { get; set; } = null; // Password cho file 7z được mã hóa
        
        // Backward compatibility
        public string HDiffPath 
        { 
            get => HDiffPaths.Length > 0 ? HDiffPaths[0] : "";
            set => HDiffPaths = string.IsNullOrEmpty(value) ? Array.Empty<string>() : new[] { value };
        }
    }

    /// <summary>
    /// Cập nhật game với các đường dẫn mặc định
    /// </summary>
    // public async Task<bool> UpdateGameAsync()
    // {
        // var config = new UpdateConfig
        // {
        //     GamePath = @"F:\StarRail",
        //     HDiffPaths = new[] { @"F:\StarRail_3.5.51_3.5.52_hdiff_seg.7z" },
        //     CurrentVersion = "3.5.51",
        //     TargetVersion = "3.5.52"
        // };

    //     // return await UpdateGameAsync(config);
    // }

    /// <summary>
    /// Cập nhật game với cấu hình tùy chỉnh
    /// </summary>
    public async Task<bool> UpdateGameAsync(UpdateConfig config)
    {
        try
        {
            // Lưu cấu hình
            await SaveConfigAsync(config);

            Console.WriteLine("=== EULA SR Game Updater ===");
            Console.WriteLine($"Game Path: {config.GamePath}");
            Console.WriteLine($"HDiff Files: {config.HDiffPaths.Length} file(s)");
            foreach (var hdiffPath in config.HDiffPaths)
            {
                Console.WriteLine($"  - {hdiffPath}");
            }
            if (!string.IsNullOrEmpty(config.UpdateFilePath))
            {
                Console.WriteLine($"Update File: {config.UpdateFilePath}");
            }
            Console.WriteLine($"Version: {config.CurrentVersion} → {config.TargetVersion}");
            Console.WriteLine();

            // Kiểm tra điều kiện trước khi cập nhật
            if (!ValidateUpdate(config))
            {
                return false;
            }

            // Tạo Progress reporter
            var progress = new Progress<string>(message =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            });

            // Thực hiện cập nhật
            var result = await _patcher.ApplyPatchesAsync(config.GamePath, config.HDiffPaths, progress, config.Password);

            if (result)
            {
                Console.WriteLine();
                Console.WriteLine("✅ Cập nhật thành công!");
                Console.WriteLine($"Game đã được cập nhật lên phiên bản {config.TargetVersion}");
                
                // Cập nhật version trong config
                config.CurrentVersion = config.TargetVersion;
                await SaveConfigAsync(config);
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("❌ Cập nhật thất bại!");
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Lỗi trong quá trình cập nhật: {ex.Message}");
            Console.WriteLine($"Chi tiết: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Cập nhật game với đường dẫn tùy chỉnh
    /// </summary>
    public async Task<bool> UpdateGameAsync(string gamePath, string hdiffPath)
    {
        return await UpdateGameAsync(gamePath, new[] { hdiffPath });
    }

    /// <summary>
    /// Cập nhật game với nhiều file hdiff
    /// </summary>
    public async Task<bool> UpdateGameAsync(string gamePath, string[] hdiffPaths)
    {
        // Tự động phát hiện version từ tên file đầu tiên
        var firstHdiffFileName = Path.GetFileNameWithoutExtension(hdiffPaths[0]);
        var versionInfo = ExtractVersionFromFileName(firstHdiffFileName);

        var config = new UpdateConfig
        {
            GamePath = gamePath,
            HDiffPaths = hdiffPaths,
            CurrentVersion = versionInfo.currentVersion,
            TargetVersion = versionInfo.targetVersion
        };

        return await UpdateGameAsync(config);
    }

    /// <summary>
    /// Cập nhật game từ file update
    /// </summary>
    public async Task<bool> UpdateGameFromUpdateFileAsync(string gamePath, string updateFilePath)
    {
        if (!File.Exists(updateFilePath))
        {
            Console.WriteLine($"❌ File update không tồn tại: {updateFilePath}");
            return false;
        }

        try
        {
            // Đọc nội dung file update
            var updateContent = await File.ReadAllTextAsync(updateFilePath);
            var hdiffPaths = new List<string>();

            // Parse file update để lấy danh sách file hdiff
            var lines = updateContent.Split('\n');
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (!string.IsNullOrEmpty(trimmedLine) && !trimmedLine.StartsWith("#"))
                {
                    // Giả sử mỗi dòng là đường dẫn đến file hdiff
                    if (File.Exists(trimmedLine))
                    {
                        hdiffPaths.Add(trimmedLine);
                    }
                }
            }

            if (hdiffPaths.Count == 0)
            {
                Console.WriteLine("❌ Không tìm thấy file hdiff nào trong file update");
                return false;
            }

            Console.WriteLine($"Tìm thấy {hdiffPaths.Count} file hdiff trong file update");

            var config = new UpdateConfig
            {
                GamePath = gamePath,
                HDiffPaths = hdiffPaths.ToArray(),
                UpdateFilePath = updateFilePath,
                CurrentVersion = "unknown",
                TargetVersion = "unknown"
            };

            return await UpdateGameAsync(config);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Lỗi khi đọc file update: {ex.Message}");
            return false;
        }
    }

    private bool ValidateUpdate(UpdateConfig config)
    {
        Console.WriteLine("Đang kiểm tra điều kiện cập nhật...");

        // Kiểm tra thư mục game
        if (!Directory.Exists(config.GamePath))
        {
            Console.WriteLine($"❌ Thư mục game không tồn tại: {config.GamePath}");
            return false;
        }

        // Kiểm tra tất cả file hdiff
        foreach (var hdiffPath in config.HDiffPaths)
        {
            if (!File.Exists(hdiffPath))
            {
                Console.WriteLine($"❌ File hdiff không tồn tại: {hdiffPath}");
                return false;
            }
        }

        // Kiểm tra dung lượng ổ đĩa
        var driveInfo = new DriveInfo(Path.GetPathRoot(config.GamePath)!);
        var totalHdiffSize = config.HDiffPaths.Sum(path => new FileInfo(path).Length);
        var requiredSpace = totalHdiffSize * 2; // Dự phòng gấp đôi

        if (driveInfo.AvailableFreeSpace < requiredSpace)
        {
            Console.WriteLine($"❌ Không đủ dung lượng ổ đĩa. Cần ít nhất {FormatBytes(requiredSpace)}");
            Console.WriteLine($"   Dung lượng khả dụng: {FormatBytes(driveInfo.AvailableFreeSpace)}");
            return false;
        }

        // Kiểm tra quyền ghi
        try
        {
            var testFile = Path.Combine(config.GamePath, "write_test.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
        }
        catch
        {
            Console.WriteLine($"❌ Không có quyền ghi vào thư mục game: {config.GamePath}");
            Console.WriteLine("   Vui lòng chạy với quyền Administrator");
            return false;
        }

        Console.WriteLine("✅ Tất cả điều kiện đã được thỏa mãn");
        return true;
    }

    private (string currentVersion, string targetVersion) ExtractVersionFromFileName(string fileName)
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

    private async Task SaveConfigAsync(UpdateConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            await File.WriteAllTextAsync(_configFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cảnh báo: Không thể lưu config - {ex.Message}");
        }
    }

    public async Task<UpdateConfig?> LoadConfigAsync()
    {
        try
        {
            if (!File.Exists(_configFile))
                return null;

            var json = await File.ReadAllTextAsync(_configFile);
            return JsonSerializer.Deserialize<UpdateConfig>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cảnh báo: Không thể đọc config - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Kiểm tra xem file 7z có được mã hóa hay không
    /// </summary>
    private async Task<bool> IsArchiveEncryptedAsync(string archivePath)
    {
        try
        {
            // Sử dụng 7-Zip để kiểm tra thông tin archive
            var sevenZipPath = Find7ZipExecutable();
            
            var startInfo = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                Arguments = $"l \"{archivePath}\"",
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

            // Kiểm tra output để xác định xem có encrypted không
            if (output.Contains("Enter password") || error.Contains("Cannot open encrypted archive") ||
                error.Contains("Wrong password") || output.Contains("*") || output.Contains("Encrypted"))
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Không thể kiểm tra trạng thái mã hóa của file: {ex.Message}");
            return false; // Giả định không encrypted nếu không kiểm tra được
        }
    }

    /// <summary>
    /// Tìm 7-Zip executable
    /// </summary>
    private string Find7ZipExecutable()
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
    /// Hiển thị thông tin về game hiện tại
    /// </summary>
    public Task ShowGameInfoAsync(string gamePath)
    {
        Console.WriteLine("=== Thông tin Game ===");
        Console.WriteLine($"Đường dẫn: {gamePath}");
        
        if (!Directory.Exists(gamePath))
        {
            Console.WriteLine("❌ Thư mục không tồn tại");
            return Task.CompletedTask;
        }

        try
        {
            var files = Directory.GetFiles(gamePath, "*", SearchOption.AllDirectories);
            var totalSize = files.Sum(f => new FileInfo(f).Length);
            
            Console.WriteLine($"Số file: {files.Length:N0}");
            Console.WriteLine($"Tổng dung lượng: {FormatBytes(totalSize)}");

            // Tìm file executable chính
            var mainExe = files.FirstOrDefault(f => 
                Path.GetFileName(f).ToLower().Contains("starrail") && 
                Path.GetExtension(f).ToLower() == ".exe");

            if (mainExe != null)
            {
                var fileInfo = new FileInfo(mainExe);
                Console.WriteLine($"File chính: {Path.GetFileName(mainExe)}");
                Console.WriteLine($"Ngày sửa đổi: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Lỗi khi đọc thông tin: {ex.Message}");
        }
        
        return Task.CompletedTask;
    }
}
