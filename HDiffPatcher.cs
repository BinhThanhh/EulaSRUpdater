using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace EulaSR;

public class HDiffPatcher
{
    private readonly string _hDiffExecutable;
    private readonly Logger _logger;
    
    public HDiffPatcher(Logger? logger = null)
    {
        _logger = logger ?? new Logger();
        
        // Tìm HDiffZ executable trong PATH hoặc thư mục hiện tại
        _hDiffExecutable = FindHDiffExecutable();
    }

    /// <summary>
    /// Áp dụng patch từ file hdiff vào thư mục game
    /// </summary>
    /// <param name="gamePath">Đường dẫn thư mục game (F:\HSR3.5.51)</param>
    /// <param name="hdiffArchivePath">Đường dẫn file hdiff 7z (F:\game_3.5.51_3.5.52_hdiff.7z)</param>
    /// <param name="progress">Callback để báo cáo tiến trình</param>
    /// <returns>True nếu patch thành công</returns>
    public async Task<bool> ApplyPatchAsync(string gamePath, string hdiffArchivePath, IProgress<string>? progress = null)
    {
        return await ApplyPatchesAsync(gamePath, new[] { hdiffArchivePath }, progress);
    }

    /// <summary>
    /// Áp dụng patch từ nhiều file hdiff vào thư mục game
    /// </summary>
    /// <param name="gamePath">Đường dẫn thư mục game</param>
    /// <param name="hdiffArchivePaths">Danh sách đường dẫn các file hdiff 7z</param>
    /// <param name="progress">Callback để báo cáo tiến trình</param>
    /// <returns>True nếu patch thành công</returns>
    public async Task<bool> ApplyPatchesAsync(string gamePath, string[] hdiffArchivePaths, IProgress<string>? progress = null)
    {
        try
        {
            _logger.LogSeparator();
            _logger.Info("Bắt đầu quá trình cập nhật game...");
            progress?.Report("Bắt đầu quá trình cập nhật game...");

            // Kiểm tra thư mục game
            if (!Directory.Exists(gamePath))
            {
                var error = $"Thư mục game không tồn tại: {gamePath}";
                _logger.Error(error);
                throw new DirectoryNotFoundException(error);
            }

            // Kiểm tra tất cả file hdiff
            foreach (var hdiffPath in hdiffArchivePaths)
            {
                if (!File.Exists(hdiffPath))
                {
                    var error = $"File hdiff không tồn tại: {hdiffPath}";
                    _logger.Error(error);
                    throw new FileNotFoundException(error);
                }
            }

            _logger.Info($"Game path: {gamePath}");
            _logger.Info($"HDiff archives: {hdiffArchivePaths.Length} files");
            foreach (var path in hdiffArchivePaths)
            {
                _logger.Info($"  - {path}");
            }

            // Tạo thư mục tạm để giải nén
            var tempDir = Path.Combine(Path.GetTempPath(), "EulaSR_HDiff_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            try
            {
                _logger.Info($"Tạo thư mục tạm: {tempDir}");
                
                // Giải nén tất cả file hdiff
                for (int i = 0; i < hdiffArchivePaths.Length; i++)
                {
                    var archivePath = hdiffArchivePaths[i];
                    progress?.Report($"Đang giải nén file hdiff {i + 1}/{hdiffArchivePaths.Length}...");
                    _logger.Info($"Bắt đầu giải nén file {i + 1}: {Path.GetFileName(archivePath)}");
                    
                    var archiveTempDir = Path.Combine(tempDir, $"archive_{i}");
                    Directory.CreateDirectory(archiveTempDir);
                    
                    await ExtractHDiffArchiveAsync(archivePath, archiveTempDir);
                    _logger.Info($"Giải nén hoàn tất file {i + 1}");
                }

                // Xử lý file delete.txt trước
                progress?.Report("Đang xóa các file cũ...");
                await ProcessDeleteFilesAsync(gamePath, tempDir);

                // Áp dụng patches
                progress?.Report("Đang áp dụng các patch...");
                _logger.Info("Bắt đầu áp dụng patches...");
                await ApplyAllPatchesAsync(gamePath, tempDir, progress);
                _logger.Info("Áp dụng patches hoàn tất");

                // Dọn dẹp file temp trong game folder
                progress?.Report("Đang dọn dẹp file tạm...");
                await CleanupTempFilesAsync(gamePath);

                progress?.Report("Cập nhật hoàn tất!");
                _logger.Info("Cập nhật game thành công!");
                return true;
            }
            finally
            {
                // Dọn dẹp thư mục tạm
                if (Directory.Exists(tempDir))
                {
                    _logger.Info($"Dọn dẹp thư mục tạm: {tempDir}");
                    Directory.Delete(tempDir, true);
                }
            }
        }
        catch (Exception ex)
        {
            var errorMsg = $"Lỗi: {ex.Message}";
            progress?.Report(errorMsg);
            _logger.Error("Cập nhật thất bại", ex);
            return false;
        }
    }

    private async Task ExtractHDiffArchiveAsync(string archivePath, string extractPath)
    {
        // Sử dụng 7-Zip command line để giải nén
        var sevenZipPath = Find7ZipExecutable();
        
        var startInfo = new ProcessStartInfo
        {
            FileName = sevenZipPath,
            Arguments = $"x \"{archivePath}\" -o\"{extractPath}\" -y",
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

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"7-Zip extraction failed: {error}");
        }
        
        _logger.Debug($"7-Zip output: {output}");
    }

    private async Task ProcessDeleteFilesAsync(string gamePath, string tempDir)
    {
        // Tìm tất cả file delete.txt trong các thư mục tạm
        var deleteFiles = Directory.GetFiles(tempDir, "delete.txt", SearchOption.AllDirectories);
        
        var allFilesToDelete = new HashSet<string>();
        
        foreach (var deleteFile in deleteFiles)
        {
            _logger.Info($"Đọc file delete: {deleteFile}");
            
            var lines = await File.ReadAllLinesAsync(deleteFile);
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (!string.IsNullOrEmpty(trimmedLine) && !trimmedLine.StartsWith("#"))
                {
                    // Chuẩn hóa đường dẫn (thay / thành \)
                    var normalizedPath = trimmedLine.Replace('/', Path.DirectorySeparatorChar);
                    allFilesToDelete.Add(normalizedPath);
                }
            }
        }

        if (allFilesToDelete.Count > 0)
        {
            _logger.Info($"Tìm thấy {allFilesToDelete.Count} file cần xóa");
            
            var deletedCount = 0;
            foreach (var fileToDelete in allFilesToDelete)
            {
                var fullPath = Path.Combine(gamePath, fileToDelete);
                
                if (File.Exists(fullPath))
                {
                    try
                    {
                        File.Delete(fullPath);
                        deletedCount++;
                        _logger.Info($"Đã xóa file: {fileToDelete}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Không thể xóa file {fileToDelete}: {ex.Message}");
                    }
                }
                else if (Directory.Exists(fullPath))
                {
                    try
                    {
                        Directory.Delete(fullPath, true);
                        deletedCount++;
                        _logger.Info($"Đã xóa thư mục: {fileToDelete}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Không thể xóa thư mục {fileToDelete}: {ex.Message}");
                    }
                }
            }
            
            _logger.Info($"Đã xóa {deletedCount}/{allFilesToDelete.Count} file/thư mục");
        }
        else
        {
            _logger.Info("Không tìm thấy file delete.txt hoặc không có file nào cần xóa");
        }
    }

    private async Task ApplyAllPatchesAsync(string gamePath, string tempDir, IProgress<string>? progress)
    {
        // Tìm và đọc hdiff map nếu có
        var hdiffMap = await LoadHDiffMapAsync(tempDir);
        
        // Tìm tất cả file .hdiff trong tất cả thư mục tạm
        var hdiffFiles = Directory.GetFiles(tempDir, "*.hdiff", SearchOption.AllDirectories);
        
        // Tìm tất cả file không phải .hdiff (các file mới)
        var newFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".hdiff", StringComparison.OrdinalIgnoreCase) && 
                       !Path.GetFileName(f).Equals("delete.txt", StringComparison.OrdinalIgnoreCase) &&
                       !Path.GetFileName(f).ToLower().Contains("hdiff_map"))
            .ToArray();
        
        var totalFiles = hdiffFiles.Length + newFiles.Length;
        
        if (totalFiles == 0)
        {
            _logger.Warning("Không tìm thấy file nào để xử lý trong archives");
            return;
        }

        _logger.Info($"Tìm thấy {hdiffFiles.Length} file patch và {newFiles.Length} file mới");
        if (hdiffMap.Count > 0)
        {
            _logger.Info($"Đã load hdiff map với {hdiffMap.Count} entries");
        }

        int currentIndex = 0;

        // Xử lý các file patch trước
        for (int i = 0; i < hdiffFiles.Length; i++)
        {
            var hdiffFile = hdiffFiles[i];
            
            // Tìm đường dẫn tương đối từ thư mục archive gốc
            var relativePath = GetRelativePathFromArchive(hdiffFile, tempDir);
            
            // Loại bỏ extension .hdiff để có đường dẫn file gốc
            var originalFileName = Path.GetFileNameWithoutExtension(hdiffFile);
            var relativeDir = Path.GetDirectoryName(relativePath) ?? "";
            var targetFilePath = Path.Combine(gamePath, relativeDir, originalFileName);

            currentIndex++;
            progress?.Report($"Đang patch file {currentIndex}/{totalFiles}: {originalFileName}");
            _logger.Info($"Patching: {originalFileName}");

            await ApplySinglePatchAsync(targetFilePath, hdiffFile, tempDir, hdiffMap);
        }

        // Xử lý các file mới
        for (int i = 0; i < newFiles.Length; i++)
        {
            var newFile = newFiles[i];
            
            // Tìm đường dẫn tương đối từ thư mục archive gốc
            var relativePath = GetRelativePathFromArchive(newFile, tempDir);
            var fileName = Path.GetFileName(newFile);
            var relativeDir = Path.GetDirectoryName(relativePath) ?? "";
            var targetFilePath = Path.Combine(gamePath, relativeDir, fileName);

            currentIndex++;
            progress?.Report($"Đang copy file mới {currentIndex}/{totalFiles}: {fileName}");
            _logger.Info($"Copying new file: {fileName}");

            await CopyNewFileAsync(newFile, targetFilePath);
        }
    }

    private string GetRelativePathFromArchive(string hdiffFilePath, string tempDir)
    {
        // Tìm thư mục archive chứa file hdiff này
        var archiveDirs = Directory.GetDirectories(tempDir, "archive_*");
        
        foreach (var archiveDir in archiveDirs)
        {
            if (hdiffFilePath.StartsWith(archiveDir))
            {
                return Path.GetRelativePath(archiveDir, hdiffFilePath);
            }
        }
        
        // Fallback: trả về relative path từ tempDir
        return Path.GetRelativePath(tempDir, hdiffFilePath);
    }

    private async Task ApplyPatchesAsync(string gamePath, string patchDir, IProgress<string>? progress)
    {
        // Tìm tất cả file .hdiff trong thư mục patch
        var hdiffFiles = Directory.GetFiles(patchDir, "*.hdiff", SearchOption.AllDirectories);
        
        if (hdiffFiles.Length == 0)
        {
            throw new InvalidOperationException("Không tìm thấy file .hdiff nào trong archive");
        }

        progress?.Report($"Tìm thấy {hdiffFiles.Length} file patch");

        for (int i = 0; i < hdiffFiles.Length; i++)
        {
            var hdiffFile = hdiffFiles[i];
            var relativePath = Path.GetRelativePath(patchDir, hdiffFile);
            
            // Loại bỏ extension .hdiff để có đường dẫn file gốc
            var originalFileName = Path.GetFileNameWithoutExtension(hdiffFile);
            var relativeDir = Path.GetDirectoryName(relativePath) ?? "";
            var targetFilePath = Path.Combine(gamePath, relativeDir, originalFileName);

            progress?.Report($"Đang patch file {i + 1}/{hdiffFiles.Length}: {originalFileName}");

            await ApplySinglePatchAsync(targetFilePath, hdiffFile, patchDir, new Dictionary<string, string>());
        }
    }

    private async Task ApplySinglePatchAsync(string targetFile, string patchFile, string tempDir, Dictionary<string, string> hdiffMap)
    {
        if (!File.Exists(targetFile))
        {
            // File gốc không tồn tại, tìm file nguồn để copy
            _logger.Info($"File gốc không tồn tại, tìm file nguồn để copy: {Path.GetFileName(targetFile)}");
            
            // Sử dụng logic tìm file nguồn cải tiến
            var sourceFile = await FindSourceFileAsync(patchFile, tempDir, hdiffMap);
            
            if (sourceFile != null)
            {
                _logger.Info($"Tìm thấy file nguồn, đang copy: {Path.GetFileName(sourceFile)}");
                await CopyNewFileAsync(sourceFile, targetFile);
                return;
            }
            else
            {
                _logger.Warning($"Không tìm thấy file nguồn, bỏ qua patch: {Path.GetFileName(targetFile)}");
                return;
            }
        }

        // Tạo file backup
        var backupFile = targetFile + ".backup";
        File.Copy(targetFile, backupFile, true);

        try
        {
            // Tạo file tạm cho kết quả
            var tempOutputFile = targetFile + ".new";

            // Chạy HDiffZ để áp dụng patch
            var startInfo = new ProcessStartInfo
            {
                FileName = _hDiffExecutable,
                Arguments = $"\"{targetFile}\" \"{patchFile}\" \"{tempOutputFile}\"",
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

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"HDiffZ failed: {error}");
            }

            // Thay thế file gốc bằng file đã patch
            File.Move(tempOutputFile, targetFile, true);
            
            // Xóa backup nếu thành công
            File.Delete(backupFile);
        }
        catch
        {
            // Khôi phục từ backup nếu có lỗi
            if (File.Exists(backupFile))
            {
                File.Move(backupFile, targetFile, true);
            }
            throw;
        }
    }

    private async Task<Dictionary<string, string>> LoadHDiffMapAsync(string tempDir)
    {
        var hdiffMap = new Dictionary<string, string>();
        
        try
        {
            // Tìm file hdiff map JSON
            var mapFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
                .Where(f => 
                {
                    var fileName = Path.GetFileName(f).ToLower();
                    return fileName.Contains("hdiff_map") || 
                           fileName.Contains("hdiffmap") ||
                           fileName.EndsWith("map.json") ||
                           fileName.EndsWith("hdiff_map.json");
                })
                .ToArray();
            
            if (mapFiles.Length == 0)
            {
                _logger.Info("Không tìm thấy file hdiff map");
                return hdiffMap;
            }
            
            var mapFile = mapFiles[0];
            _logger.Info($"Đọc hdiff map từ: {Path.GetFileName(mapFile)}");
            
            var fileContent = await File.ReadAllTextAsync(mapFile);
            
            // Thử đọc như JSON trước
            if (Path.GetFileName(mapFile).ToLower().EndsWith(".json"))
            {
                try
                {
                    using var jsonDoc = JsonDocument.Parse(fileContent);
                    foreach (var element in jsonDoc.RootElement.EnumerateObject())
                    {
                        var patchFile = element.Name;
                        var sourceFile = element.Value.GetString() ?? "";
                        if (!string.IsNullOrEmpty(sourceFile))
                        {
                            hdiffMap[patchFile] = sourceFile;
                            _logger.Debug($"JSON Map entry: {patchFile} -> {sourceFile}");
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.Warning($"Lỗi đọc JSON, thử format text: {ex.Message}");
                    // Fallback to text format
                    ParseTextFormat(fileContent, hdiffMap);
                }
            }
            else
            {
                // Đọc format text
                ParseTextFormat(fileContent, hdiffMap);
            }
            
            _logger.Info($"Đã load {hdiffMap.Count} map entries");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Lỗi khi đọc hdiff map: {ex.Message}");
        }
        
        return hdiffMap;
    }

    private async Task<string?> FindSourceFileAsync(string patchFile, string tempDir, Dictionary<string, string> hdiffMap)
    {
        var patchFileName = Path.GetFileName(patchFile);
        var baseFileName = Path.GetFileNameWithoutExtension(patchFile);
        
        // Chiến lược 1: Sử dụng hdiff map
        if (hdiffMap.ContainsKey(patchFileName))
        {
            var mappedFileName = hdiffMap[patchFileName];
            
            var mappedFiles = Directory.GetFiles(tempDir, mappedFileName, SearchOption.AllDirectories);
            if (mappedFiles.Length > 0)
            {
                return mappedFiles[0];
            }
        }

        // Chiến lược 2: Tìm file có cùng tên base (bỏ .hdiff)
        var baseNameFiles = Directory.GetFiles(tempDir, baseFileName, SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".hdiff", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        
        if (baseNameFiles.Length > 0)
        {
            return baseNameFiles[0];
        }

        // Chiến lược 3: Tìm với các extension phổ biến
        var commonExtensions = new[] { ".block", ".bin", ".data", ".asset", ".unity3d", ".bytes" };
        foreach (var ext in commonExtensions)
        {
            var searchName = baseFileName + ext;
            
            var extFiles = Directory.GetFiles(tempDir, searchName, SearchOption.AllDirectories);
            if (extFiles.Length > 0)
            {
                return extFiles[0];
            }
        }

        // Chiến lược 4: Tìm pattern với wildcard
        var searchPattern = baseFileName + ".*";
        
        var allFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
            .Where(f => 
            {
                var fileName = Path.GetFileNameWithoutExtension(f);
                return fileName.Equals(baseFileName, StringComparison.OrdinalIgnoreCase) &&
                       !f.EndsWith(".hdiff", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        if (allFiles.Length > 0)
        {
            return allFiles[0];
        }

        // Chiến lược 5: Tìm kiếm fuzzy (tên tương tự)
        if (baseFileName.Length >= 8) // Chỉ với tên đủ dài
        {
            var prefix = baseFileName.Substring(0, Math.Min(8, baseFileName.Length));
            
            var fuzzyFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
                .Where(f => 
                {
                    var fileName = Path.GetFileNameWithoutExtension(f);
                    return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                           !f.EndsWith(".hdiff", StringComparison.OrdinalIgnoreCase);
                })
                .ToArray();

            if (fuzzyFiles.Length > 0)
            {
                return fuzzyFiles[0];
            }
        }

        // Log tất cả file có trong tempDir để debug
        var allTempFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".hdiff", StringComparison.OrdinalIgnoreCase))
            .Take(10) // Chỉ log 10 file đầu
            .ToArray();
            
        foreach (var file in allTempFiles)
        {
        }
        
        if (allTempFiles.Length == 0)
        {
        }

        return null;
    }

    private void ParseTextFormat(string fileContent, Dictionary<string, string> hdiffMap)
    {
        var lines = fileContent.Split('\n');
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                continue;
            
            // Format có thể là: patch_file -> source_file hoặc patch_file=source_file
            var parts = trimmedLine.Split(new[] { "->", "=", "\t", " " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var patchFile = parts[0].Trim();
                var sourceFile = parts[1].Trim();
                hdiffMap[patchFile] = sourceFile;
                _logger.Debug($"Text Map entry: {patchFile} -> {sourceFile}");
            }
        }
    }

    private async Task CleanupTempFilesAsync(string gamePath)
    {
        try
        {
            _logger.Info("Bắt đầu dọn dẹp file tạm trong game folder...");
            
            var tempFilesToDelete = new[]
            {
                "deletefiles.txt",           // File danh sách xóa
                "hdiffmap.json",       // File map JSON (chính)  
            };

            var deletedCount = 0;
            
            // Tìm và xóa file temp trong game folder
            foreach (var tempFileName in tempFilesToDelete)
            {
                var tempFiles = Directory.GetFiles(gamePath, tempFileName, SearchOption.AllDirectories);
                
                foreach (var tempFile in tempFiles)
                {
                    try
                    {
                        await Task.Run(() => File.Delete(tempFile));
                        deletedCount++;
                        var relativePath = Path.GetRelativePath(gamePath, tempFile);
                        _logger.Info($"🗑️ Đã xóa file tạm: {relativePath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"❌ Không thể xóa file tạm {Path.GetFileName(tempFile)}: {ex.Message}");
                    }
                }
            }

            // Tìm và xóa các file backup (.backup)
            var backupFiles = Directory.GetFiles(gamePath, "*.backup", SearchOption.AllDirectories);
            foreach (var backupFile in backupFiles)
            {
                try
                {
                    await Task.Run(() => File.Delete(backupFile));
                    deletedCount++;
                    _logger.Debug($"Đã xóa file backup: {Path.GetRelativePath(gamePath, backupFile)}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Không thể xóa file backup {backupFile}: {ex.Message}");
                }
            }

            if (deletedCount > 0)
            {
                _logger.Info($"Đã dọn dẹp {deletedCount} file tạm");
            }
            else
            {
                _logger.Info("Không có file tạm nào cần dọn dẹp");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Lỗi khi dọn dẹp file tạm: {ex.Message}");
        }
    }

    private async Task CopyNewFileAsync(string sourceFile, string targetFile)
    {
        try
        {
            // Tạo thư mục đích nếu chưa có
            var targetDir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
                _logger.Info($"Tạo thư mục: {targetDir}");
            }

            // Copy file mới
            await Task.Run(() => File.Copy(sourceFile, targetFile, true));
            _logger.Info($"Đã copy file mới: {Path.GetFileName(targetFile)}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Lỗi khi copy file {Path.GetFileName(sourceFile)}: {ex.Message}");
            throw;
        }
    }

    private string FindHDiffExecutable()
    {
        var possibleNames = new[] { "hdiffz.exe", "hdiffz", "hpatchz.exe", "hpatchz" };
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        
        var searchPaths = new[]
        {
            baseDir,                                    // Thư mục chứa exe
            Path.Combine(baseDir, "tools"),            // Thư mục tools
            Environment.CurrentDirectory,               // Thư mục hiện tại
            Path.Combine(Environment.CurrentDirectory, "tools")
        };

        // Tìm trong các thư mục project trước
        foreach (var searchPath in searchPaths)
        {
            foreach (var name in possibleNames)
            {
                var fullPath = Path.Combine(searchPath, name);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
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
                    _logger.Info($"Tìm thấy HDiffZ trong PATH: {fullPath}");
                    return fullPath;
                }
            }
        }

        // Log các đường dẫn đã tìm
        _logger.Error("Không tìm thấy HDiffZ executable. Đã tìm trong:");
        foreach (var searchPath in searchPaths)
        {
            _logger.Error($"  - {searchPath}");
        }
        
        throw new FileNotFoundException($"Không tìm thấy HDiffZ executable. Vui lòng đặt hdiffz.exe vào một trong các thư mục sau:\n" +
            $"- {baseDir}\n" +
            $"- {Path.Combine(baseDir, "tools")}\n" +
            $"- {Environment.CurrentDirectory}\n" +
            $"Hoặc thêm vào PATH.");
    }

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
}
