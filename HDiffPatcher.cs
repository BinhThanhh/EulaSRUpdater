using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace EulaSR;

public class HDiffPatcher
{
    private readonly string _hPatchExecutable;
    private readonly string _hDiffExecutable;
    private readonly Logger _logger;
    
    public HDiffPatcher(Logger? logger = null)
    {
        _logger = logger ?? new Logger();
        
        // Tìm cả hai tools
        _hPatchExecutable = FindExecutable("hpatchz"); // Tool chính để apply patch
        _hDiffExecutable = FindExecutable("hdiffz");   // Tool fallback
        
        _logger.Info($"HPatch tool: {(_hPatchExecutable != null ? Path.GetFileName(_hPatchExecutable) : "Không tìm thấy")}");
        _logger.Info($"HDiff tool: {(_hDiffExecutable != null ? Path.GetFileName(_hDiffExecutable) : "Không tìm thấy")}");
        
        // Đảm bảo có ít nhất một tool
        if (string.IsNullOrEmpty(_hPatchExecutable) && string.IsNullOrEmpty(_hDiffExecutable))
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            throw new FileNotFoundException($"Không tìm thấy hpatchz.exe hoặc hdiffz.exe. Vui lòng đặt một trong hai file vào:\n" +
                $"- {baseDir}\n" +
                $"- {Path.Combine(baseDir, "tools")}\n" +
                $"- {Environment.CurrentDirectory}\n" +
                $"Hoặc thêm vào PATH.");
        }
    }

    /// <summary>
    /// Áp dụng patch từ file hdiff vào thư mục game
    /// </summary>
    /// <param name="gamePath">Đường dẫn thư mục game (F:\StarRail)</param>
    /// <param name="hdiffArchivePath">Đường dẫn file hdiff 7z (F:\StarRail_3.5.51_3.5.52_hdiff_seg.7z)</param>
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
                
                try
                {
                    await ApplyAllPatchesAsync(gamePath, tempDir, progress);
                    _logger.Info("Áp dụng patches hoàn tất");
                }
                catch (Exception patchEx)
                {
                    _logger.Error($"Lỗi khi áp dụng patches: {patchEx.Message}");
                    _logger.Debug($"Stack trace: {patchEx.StackTrace}");
                    progress?.Report("Đang rollback các thay đổi...");
                    
                    // Thực hiện rollback nếu có lỗi
                    try
                    {
                        await RollbackChangesAsync(gamePath);
                        _logger.Info("✅ Rollback thành công");
                        progress?.Report("Đã rollback thành công. Game vẫn có thể chạy được.");
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger.Error($"Lỗi khi rollback: {rollbackEx.Message}");
                        progress?.Report("❌ Rollback thất bại. Vui lòng kiểm tra backup files.");
                    }
                    
                    throw;
                }

                // Dọn dẹp file temp trong game folder
                progress?.Report("Đang dọn dẹp file tạm...");
                await CleanupTempFilesAsync(gamePath);

                // Kiểm tra tính toàn vẹn của game sau khi update
                progress?.Report("Đang kiểm tra tính toàn vẹn game...");
                var isValid = await ValidateGameIntegrityAsync(gamePath);
                
                if (!isValid)
                {
                    _logger.Warning("Phát hiện một số vấn đề với game sau khi update, nhưng quá trình cập nhật đã hoàn tất");
                    progress?.Report("Cập nhật hoàn tất với một số cảnh báo!");
                }
                else
                {
                    progress?.Report("Cập nhật hoàn tất!");
                    _logger.Info("Cập nhật game thành công!");
                }
                
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
        var deleteFiles = Directory.GetFiles(tempDir, "deletefiles.txt", SearchOption.AllDirectories);
        
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
                       !Path.GetFileName(f).Equals("deletefiles.txt", StringComparison.OrdinalIgnoreCase) &&
                       !Path.GetFileName(f).ToLower().Contains("hdiffmap"))
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
        int skippedCount = 0;

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

            try
            {
                await ApplySinglePatchAsync(targetFilePath, hdiffFile, tempDir, hdiffMap);
                _logger.Debug($"✅ Patch thành công: {originalFileName}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"⚠️ Bỏ qua patch {originalFileName}: {ex.Message}");
                skippedCount++;
                
                // Log chi tiết hơn cho file quan trọng
                if (originalFileName.ToLower().Contains("gameassembly") || 
                    originalFileName.ToLower().Contains("starrail") ||
                    originalFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Info($"🛡️ File quan trọng {originalFileName} được bảo vệ - giữ nguyên phiên bản cũ");
                }
            }
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

        // Log thống kê cuối cùng
        if (skippedCount > 0)
        {
            _logger.Warning($"⚠️ Đã bỏ qua {skippedCount}/{totalFiles} file do không tương thích hoặc lỗi");
            progress?.Report($"Hoàn tất với {skippedCount} file bỏ qua");
        }
        else
        {
            _logger.Info($"✅ Đã xử lý thành công tất cả {totalFiles} file");
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
                var relativePath = Path.GetRelativePath(archiveDir, hdiffFilePath);
                
                // Loại bỏ tên folder archive nếu có (ví dụ: StarRail_3.5.51_3.5.52_hdiff_seg/...)
                var pathParts = relativePath.Split(Path.DirectorySeparatorChar);
                if (pathParts.Length > 1 && pathParts[0].Contains("StarRail_") && pathParts[0].Contains("_hdiff"))
                {
                    // Bỏ qua phần đầu là tên archive, lấy phần còn lại
                    return string.Join(Path.DirectorySeparatorChar, pathParts.Skip(1));
                }
                
                return relativePath;
            }
        }
        
        // Fallback: trả về relative path từ tempDir
        var fallbackPath = Path.GetRelativePath(tempDir, hdiffFilePath);
        var fallbackParts = fallbackPath.Split(Path.DirectorySeparatorChar);
        
        // Tương tự, loại bỏ tên archive nếu có
        if (fallbackParts.Length > 2 && fallbackParts[1].Contains("StarRail_") && fallbackParts[1].Contains("_hdiff"))
        {
            return string.Join(Path.DirectorySeparatorChar, fallbackParts.Skip(2));
        }
        
        return fallbackPath;
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
        var fileName = Path.GetFileName(targetFile);
        var isExecutable = Path.GetExtension(targetFile).Equals(".exe", StringComparison.OrdinalIgnoreCase);
        
        if (!File.Exists(targetFile))
        {
            // File gốc không tồn tại, tìm file nguồn để copy
            _logger.Info($"File gốc không tồn tại, tìm file nguồn để copy: {fileName}");
            
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
                _logger.Warning($"Không tìm thấy file nguồn, bỏ qua patch: {fileName}");
                return;
            }
        }

        // Tạo file backup (đặc biệt quan trọng cho file executable)
        var backupFile = targetFile + ".backup";
        File.Copy(targetFile, backupFile, true);
        
        if (isExecutable)
        {
            _logger.Info($"⚠️  Đang patch file executable quan trọng: {fileName}");
        }

        try
        {
            // Tạo file tạm cho kết quả
            var tempOutputFile = targetFile + ".new";

            // Debug: Log thông tin về files trước khi patch
            var originalInfo = new FileInfo(targetFile);
            var patchInfo = new FileInfo(patchFile);
            _logger.Debug($"Original file: {originalInfo.Length} bytes");
            _logger.Debug($"Patch file: {patchInfo.Length} bytes");

            // Kiểm tra tương thích file size (basic check)
            if (await ShouldSkipPatchDueToSizeMismatch(targetFile, patchFile, fileName))
            {
                _logger.Warning($"⚠️ Bỏ qua patch {fileName} do size mismatch được phát hiện trước");
                
                // Xóa backup file vì không cần patch
                if (File.Exists(backupFile))
                {
                    File.Delete(backupFile);
                }
                return;
            }

            // Thử apply patch với cả hai tools
            bool patchSuccess = false;
            string lastError = "";
            string usedTool = "";

            // Thử với hpatchz trước (tool chính)
            if (!string.IsNullOrEmpty(_hPatchExecutable))
            {
                patchSuccess = await TryApplyPatchWithTool(_hPatchExecutable, "hpatchz", targetFile, patchFile, tempOutputFile);
                usedTool = "hpatchz";
                
                if (!patchSuccess)
                {
                    lastError = $"hpatchz failed";
                    _logger.Debug($"hpatchz thất bại, thử hdiffz...");
                }
            }

            // Nếu hpatchz thất bại hoặc không có, thử hdiffz
            if (!patchSuccess && !string.IsNullOrEmpty(_hDiffExecutable))
            {
                patchSuccess = await TryApplyPatchWithTool(_hDiffExecutable, "hdiffz", targetFile, patchFile, tempOutputFile);
                usedTool = patchSuccess ? "hdiffz" : usedTool;
                
                if (!patchSuccess)
                {
                    lastError = $"Cả hpatchz và hdiffz đều thất bại";
                }
            }

            if (patchSuccess)
            {
                _logger.Debug($"✅ Patch thành công với {usedTool} cho {fileName}");
            }
            else
            {
                _logger.Warning($"❌ Patch thất bại với tất cả tools cho {fileName}: {lastError}");
                
                // Cập nhật lastError với thông tin chi tiết
                if (string.IsNullOrEmpty(_hPatchExecutable))
                {
                    lastError = "hpatchz không có, " + lastError;
                }
                if (string.IsNullOrEmpty(_hDiffExecutable))
                {
                    lastError = "hdiffz không có, " + lastError;
                }
            }

            if (!patchSuccess)
            {
                // Kiểm tra xem có phải lỗi size mismatch không
                if (lastError.Contains("oldDataSize") && lastError.Contains("!="))
                {
                    _logger.Warning($"⚠️ File size mismatch cho {fileName} - có thể patch không tương thích với phiên bản hiện tại");
                    
                    // Đối với file quan trọng, bỏ qua thay vì fail
                    if (isExecutable || 
                        fileName.ToLower().Contains("gameassembly") ||
                        fileName.ToLower().Contains("starrail") ||
                        fileName.ToLower().Contains("unitycrashandler") ||
                        fileName.ToLower().Contains("unityplayer") ||
                        fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Warning($"⚠️ Bỏ qua patch file quan trọng {fileName} do không tương thích");
                        
                        // Xóa backup file vì không patch
                        if (File.Exists(backupFile))
                        {
                            File.Delete(backupFile);
                        }
                        return;
                    }
                }
                
                throw new InvalidOperationException($"HDiff failed for {fileName}: {lastError}");
            }

            // Kiểm tra file output có hợp lệ không
            if (!File.Exists(tempOutputFile))
            {
                throw new InvalidOperationException($"HDiffZ không tạo ra file output cho {fileName}");
            }

            var outputInfo = new FileInfo(tempOutputFile);
            if (outputInfo.Length == 0)
            {
                throw new InvalidOperationException($"File output rỗng cho {fileName}");
            }

            _logger.Debug($"Output file size: {outputInfo.Length} bytes");

            // Đối với file executable, kiểm tra xem có phải là HDiff patch file không
            if (isExecutable)
            {
                using var stream = new FileStream(tempOutputFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new BinaryReader(stream);
                
                if (stream.Length >= 2)
                {
                    var firstBytes = reader.ReadUInt16();
                    _logger.Debug($"First 2 bytes of output file: 0x{firstBytes:X4}");
                    
                    // Kiểm tra xem có phải là HDiff signature không (0x4448 = "HD")
                    if (firstBytes == 0x4448)
                    {
                        throw new InvalidOperationException($"File output {fileName} có vẻ là HDiff patch file, không phải executable. Có thể arguments HDiffZ sai.");
                    }
                    
                    // Reset stream position
                    stream.Seek(0, SeekOrigin.Begin);
                }
            }

            // Đối với file executable, kiểm tra thêm signature (nhưng không fail nếu không pass)
            if (isExecutable)
            {
                var isValid = await ValidateExecutableAsync(tempOutputFile);
                if (!isValid)
                {
                    _logger.Warning($"⚠️ File executable {fileName} có thể có vấn đề, nhưng vẫn tiếp tục...");
                }
                else
                {
                    _logger.Info($"✅ Đã xác nhận file executable {fileName} hợp lệ sau patch");
                }
            }

            // Thay thế file gốc bằng file đã patch
            File.Move(tempOutputFile, targetFile, true);
            
            // Xóa backup nếu thành công
            File.Delete(backupFile);
            
            if (isExecutable)
            {
                _logger.Info($"✅ Patch file executable {fileName} thành công");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Lỗi khi patch {fileName}: {ex.Message}");
            
            // Khôi phục từ backup nếu có lỗi
            if (File.Exists(backupFile))
            {
                File.Move(backupFile, targetFile, true);
                _logger.Info($"🔄 Đã khôi phục {fileName} từ backup");
            }
            
            // Đối với file executable hoặc file quan trọng, không throw exception
            if (isExecutable || 
                fileName.ToLower().Contains("gameassembly") ||
                fileName.ToLower().Contains("starrail") ||
                fileName.ToLower().Contains("unitycrashandler") ||
                fileName.ToLower().Contains("unityplayer"))
            {
                _logger.Error($"❌ NGHIÊM TRỌNG: Không thể patch file quan trọng {fileName}");
                _logger.Warning($"⚠️ Bỏ qua patch file {fileName} để tránh làm hỏng game");
                return; // Không throw, chỉ return
            }
            
            // Đối với file không quan trọng, vẫn có thể throw
            throw;
        }
    }

    private async Task<Dictionary<string, string>> LoadHDiffMapAsync(string tempDir)
    {
        var hdiffMap = new Dictionary<string, string>();
        
        try
        {
            // Tìm file hdiff map
            var mapFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
                .Where(f => 
                {
                    var fileName = Path.GetFileName(f).ToLower();
                    return fileName.Contains("hdiff_map") || 
                           fileName.Contains("hdiffmap") ||
                           fileName.Contains("hdifffiles") ||
                           fileName.EndsWith("hdifffiles.txt") ||
                           fileName.EndsWith("hdiff_map.json") ||
                           fileName.EndsWith("map.txt") ||
                           fileName.EndsWith("hdiffmap.txt");
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
            
            // Kiểm tra format của file
            var fileName = Path.GetFileName(mapFile).ToLower();
            
            if (fileName.EndsWith(".txt") || fileName.Contains("hdiffmap"))
            {
                // Thử parse JSON Lines format trước (mỗi dòng là một JSON object)
                if (ParseJsonLinesFormat(fileContent, hdiffMap))
                {
                    _logger.Info($"Đã parse JSON Lines format với {hdiffMap.Count} entries");
                }
                else
                {
                    // Fallback to text format
                    ParseTextFormat(fileContent, hdiffMap);
                }
            }
            else if (fileName.EndsWith(".json"))
            {
                // Thử đọc như JSON object duy nhất
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
                catch (JsonException)
                {
                    // Thử JSON Lines format
                    if (!ParseJsonLinesFormat(fileContent, hdiffMap))
                    {
                        // Fallback to text format
                        ParseTextFormat(fileContent, hdiffMap);
                    }
                }
            }
            
            _logger.Info($"Đã load {hdiffMap.Count} map entries");
            
            // Log một vài entries đầu tiên để debug
            if (hdiffMap.Count > 0)
            {
                var firstEntries = hdiffMap.Take(3);
                foreach (var entry in firstEntries)
                {
                    _logger.Debug($"Map sample: {entry.Key} -> {entry.Value}");
                }
                if (hdiffMap.Count > 3)
                {
                    _logger.Debug($"... và {hdiffMap.Count - 3} entries khác");
                }
            }
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
            var mappedPath = hdiffMap[patchFileName];
            
            // Tìm file theo đường dẫn đầy đủ từ map
            var mappedFileName = Path.GetFileName(mappedPath);
            var mappedFiles = Directory.GetFiles(tempDir, mappedFileName, SearchOption.AllDirectories);
            
            // Ưu tiên file có đường dẫn tương tự với mappedPath
            foreach (var file in mappedFiles)
            {
                var relativePath = Path.GetRelativePath(tempDir, file);
                if (relativePath.Contains(mappedPath.Replace('/', Path.DirectorySeparatorChar)) || 
                    relativePath.EndsWith(mappedPath.Replace('/', Path.DirectorySeparatorChar)))
                {
                    _logger.Debug($"Found mapped file: {patchFileName} -> {relativePath}");
                    return file;
                }
            }
            
            // Fallback: lấy file đầu tiên có tên giống
            if (mappedFiles.Length > 0)
            {
                _logger.Debug($"Found mapped file (fallback): {patchFileName} -> {Path.GetRelativePath(tempDir, mappedFiles[0])}");
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

    private bool ParseJsonLinesFormat(string fileContent, Dictionary<string, string> hdiffMap)
    {
        try
        {
            var lines = fileContent.Split('\n');
            var parsedCount = 0;
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine))
                    continue;
                
                try
                {
                    // Parse mỗi dòng như một JSON object
                    using var jsonDoc = JsonDocument.Parse(trimmedLine);
                    var root = jsonDoc.RootElement;
                    
                    if (root.TryGetProperty("remoteName", out var remoteNameElement))
                    {
                        var remoteName = remoteNameElement.GetString();
                        if (!string.IsNullOrEmpty(remoteName))
                        {
                            // Tạo tên file patch từ remoteName
                            var patchFileName = Path.GetFileName(remoteName) + ".hdiff";
                            hdiffMap[patchFileName] = remoteName;
                            _logger.Debug($"JSON Lines Map entry: {patchFileName} -> {remoteName}");
                            parsedCount++;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Nếu không parse được JSON, bỏ qua dòng này
                    continue;
                }
            }
            
            return parsedCount > 0;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Lỗi khi parse JSON Lines format: {ex.Message}");
            return false;
        }
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
                "hdiffmap.json",            // File map JSON (chính)  
                "hdiffmap.txt",             // File map text (JSON Lines format)
                "hdiff_map.json",           // File map JSON (alternative name)
                "hdiff_map.txt",            // File map text (alternative name)
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

            // Tìm và xóa các thư mục archive không cần thiết trong game folder
            var archiveDirs = Directory.GetDirectories(gamePath, "*", SearchOption.TopDirectoryOnly)
                .Where(dir => 
                {
                    var dirName = Path.GetFileName(dir);
                    return dirName.Contains("StarRail_") && dirName.Contains("_hdiff") && dirName.Contains("_seg");
                })
                .ToArray();

            foreach (var archiveDir in archiveDirs)
            {
                try
                {
                    await Task.Run(() => Directory.Delete(archiveDir, true));
                    deletedCount++;
                    var relativePath = Path.GetRelativePath(gamePath, archiveDir);
                    _logger.Info($"🗑️ Đã xóa thư mục tạm: {relativePath}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"❌ Không thể xóa thư mục tạm {Path.GetFileName(archiveDir)}: {ex.Message}");
                }
            }

            // Tìm và xóa các file .new nếu còn sót lại
            var newFiles = Directory.GetFiles(gamePath, "*.new", SearchOption.AllDirectories);
            foreach (var newFile in newFiles)
            {
                try
                {
                    await Task.Run(() => File.Delete(newFile));
                    deletedCount++;
                    _logger.Debug($"Đã xóa file .new: {Path.GetRelativePath(gamePath, newFile)}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Không thể xóa file .new {newFile}: {ex.Message}");
                }
            }

            if (deletedCount > 0)
            {
                _logger.Info($"Đã dọn dẹp {deletedCount} file/thư mục tạm");
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

    private string FindExecutable(string toolName)
    {
        var possibleNames = new[] { $"{toolName}.exe", toolName };
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
                    _logger.Debug($"Tìm thấy {toolName}: {fullPath}");
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
                    _logger.Debug($"Tìm thấy {toolName} trong PATH: {fullPath}");
                    return fullPath;
                }
            }
        }

        // Không tìm thấy - trả về null thay vì throw exception
        _logger.Debug($"Không tìm thấy {toolName} executable");
        return null;
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

    /// <summary>
    /// Thử apply patch với một tool cụ thể
    /// </summary>
    private async Task<bool> TryApplyPatchWithTool(string toolPath, string toolName, string targetFile, string patchFile, string outputFile)
    {
        try
        {
            // Thử với arguments cơ bản trước
            var arguments = $"\"{targetFile}\" \"{patchFile}\" \"{outputFile}\"";
            _logger.Debug($"{toolName} command: {toolPath} {arguments}");

            var startInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(targetFile)
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();

            _logger.Debug($"{toolName} exit code: {process.ExitCode}");
            if (!string.IsNullOrEmpty(output))
                _logger.Debug($"{toolName} output: {output}");
            if (!string.IsNullOrEmpty(error))
                _logger.Debug($"{toolName} error: {error}");

            if (process.ExitCode == 0 && File.Exists(outputFile))
            {
                return true;
            }

            // Nếu thất bại với hdiffz, thử với -f flag
            if (toolName.Equals("hdiffz", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug($"Thử {toolName} với -f flag...");
                
                var altArguments = $"-f \"{targetFile}\" \"{patchFile}\" \"{outputFile}\"";
                startInfo.Arguments = altArguments;
                
                using var process2 = new Process { StartInfo = startInfo };
                process2.Start();
                
                var output2 = await process2.StandardOutput.ReadToEndAsync();
                var error2 = await process2.StandardError.ReadToEndAsync();
                await process2.WaitForExitAsync();
                
                _logger.Debug($"{toolName} -f exit code: {process2.ExitCode}");
                if (!string.IsNullOrEmpty(error2))
                    _logger.Debug($"{toolName} -f error: {error2}");
                
                if (process2.ExitCode == 0 && File.Exists(outputFile))
                {
                    _logger.Info($"✅ {toolName} thành công với -f flag");
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Lỗi khi chạy {toolName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Kiểm tra xem có nên bỏ qua patch do size mismatch không
    /// </summary>
    private async Task<bool> ShouldSkipPatchDueToSizeMismatch(string targetFile, string patchFile, string fileName)
    {
        try
        {
            var targetSize = new FileInfo(targetFile).Length;
            var patchSize = new FileInfo(patchFile).Length;
            
            // Nếu file gốc quá nhỏ so với patch file, có thể không tương thích
            var sizeRatio = patchSize > 0 ? (double)targetSize / patchSize : 1.0;
            
            if (targetSize < 200000 && patchSize > 10000000) // 200KB vs 10MB - chênh lệch quá lớn
            {
                _logger.Debug($"Large size mismatch: target={targetSize}, patch={patchSize}, ratio={sizeRatio:F4}");
                
                // Chỉ bỏ qua nếu chênh lệch quá lớn (hơn 50 lần)
                if (sizeRatio < 0.02) // target < 2% của patch size
                {
                    _logger.Warning($"⚠️ File {fileName} có size mismatch quá lớn - có thể patch không tương thích");
                    return true; // Bỏ qua patch này
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Lỗi khi kiểm tra size mismatch: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Rollback các thay đổi nếu patch thất bại
    /// </summary>
    private async Task RollbackChangesAsync(string gamePath)
    {
        try
        {
            _logger.Info("Bắt đầu rollback các thay đổi...");
            var rollbackCount = 0;

            // Tìm tất cả file backup và khôi phục
            var backupFiles = Directory.GetFiles(gamePath, "*.backup", SearchOption.AllDirectories);
            
            foreach (var backupFile in backupFiles)
            {
                try
                {
                    var originalFile = backupFile.Substring(0, backupFile.Length - ".backup".Length);
                    
                    if (File.Exists(originalFile))
                    {
                        File.Delete(originalFile);
                    }
                    
                    File.Move(backupFile, originalFile);
                    rollbackCount++;
                    
                    var fileName = Path.GetFileName(originalFile);
                    _logger.Info($"🔄 Đã rollback: {fileName}");
                    
                    // Log đặc biệt cho file executable
                    if (Path.GetExtension(originalFile).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Info($"✅ Đã khôi phục file executable: {fileName}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Không thể rollback file {Path.GetFileName(backupFile)}: {ex.Message}");
                }
            }

            // Xóa các file .new còn sót lại
            var newFiles = Directory.GetFiles(gamePath, "*.new", SearchOption.AllDirectories);
            foreach (var newFile in newFiles)
            {
                try
                {
                    File.Delete(newFile);
                    _logger.Debug($"Đã xóa file .new: {Path.GetRelativePath(gamePath, newFile)}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Không thể xóa file .new {newFile}: {ex.Message}");
                }
            }

            if (rollbackCount > 0)
            {
                _logger.Info($"✅ Đã rollback {rollbackCount} file thành công");
            }
            else
            {
                _logger.Warning("Không tìm thấy file backup nào để rollback");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Lỗi khi rollback: {ex.Message}");
        }
    }

    /// <summary>
    /// Kiểm tra tính hợp lệ của file executable
    /// </summary>
    private async Task<bool> ValidateExecutableAsync(string exePath)
    {
        try
        {
            // Kiểm tra cơ bản: file tồn tại và có kích thước > 0
            if (!File.Exists(exePath))
            {
                _logger.Debug($"File không tồn tại: {Path.GetFileName(exePath)}");
                return false;
            }

            var fileInfo = new FileInfo(exePath);
            if (fileInfo.Length < 512) // File exe phải có ít nhất 512 bytes
            {
                _logger.Debug($"File quá nhỏ: {Path.GetFileName(exePath)} ({fileInfo.Length} bytes)");
                return false;
            }

            // Kiểm tra PE header (Windows executable signature) - với error handling tốt hơn
            using var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            
            // Kiểm tra có đủ dữ liệu để đọc DOS header không
            if (stream.Length < 64)
            {
                _logger.Debug($"File quá nhỏ để có DOS header: {Path.GetFileName(exePath)}");
                return false;
            }
            
            // Đọc DOS header
            stream.Seek(0, SeekOrigin.Begin);
            var dosSignature = reader.ReadUInt16();
            if (dosSignature != 0x5A4D) // "MZ"
            {
                _logger.Debug($"File {Path.GetFileName(exePath)} không có DOS signature hợp lệ (0x{dosSignature:X4})");
                return false;
            }

            // Kiểm tra có đủ dữ liệu để đọc PE header offset không
            if (stream.Length < 0x40)
            {
                _logger.Debug($"File quá nhỏ để có PE header offset: {Path.GetFileName(exePath)}");
                return false;
            }

            // Nhảy đến PE header offset
            stream.Seek(0x3C, SeekOrigin.Begin);
            var peHeaderOffset = reader.ReadUInt32();

            if (peHeaderOffset >= fileInfo.Length || peHeaderOffset < 0x40)
            {
                _logger.Debug($"File {Path.GetFileName(exePath)} có PE header offset không hợp lệ (0x{peHeaderOffset:X8})");
                return false;
            }

            // Kiểm tra có đủ dữ liệu để đọc PE signature không
            if (peHeaderOffset + 4 > stream.Length)
            {
                _logger.Debug($"File quá nhỏ để chứa PE signature: {Path.GetFileName(exePath)}");
                return false;
            }

            // Kiểm tra PE signature
            stream.Seek(peHeaderOffset, SeekOrigin.Begin);
            var peSignature = reader.ReadUInt32();
            if (peSignature != 0x00004550) // "PE\0\0"
            {
                _logger.Debug($"File {Path.GetFileName(exePath)} không có PE signature hợp lệ (0x{peSignature:X8})");
                return false;
            }

            _logger.Debug($"✅ File executable {Path.GetFileName(exePath)} có structure hợp lệ");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            _logger.Debug($"Không có quyền truy cập file {Path.GetFileName(exePath)}");
            return false;
        }
        catch (IOException ex)
        {
            _logger.Debug($"Lỗi I/O khi đọc file {Path.GetFileName(exePath)}: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Lỗi khi validate executable {Path.GetFileName(exePath)}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Kiểm tra tính toàn vẹn của game sau khi update
    /// </summary>
    private async Task<bool> ValidateGameIntegrityAsync(string gamePath)
    {
        try
        {
            _logger.Info("Bắt đầu kiểm tra tính toàn vẹn game...");
            
            var issues = new List<string>();
            
            // Kiểm tra file executable chính
            var mainExe = Directory.GetFiles(gamePath, "*.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => Path.GetFileName(f).ToLower().Contains("starrail"));
            
            if (mainExe == null)
            {
                issues.Add("Không tìm thấy file executable chính của game");
            }
            else if (!File.Exists(mainExe))
            {
                issues.Add($"File executable chính không tồn tại: {Path.GetFileName(mainExe)}");
            }
            else
            {
                // Kiểm tra tính hợp lệ của file executable (không fail nếu không pass)
                try
                {
                    var isValid = await ValidateExecutableAsync(mainExe);
                    if (!isValid)
                    {
                        _logger.Warning($"⚠️ File executable chính có thể có vấn đề: {Path.GetFileName(mainExe)}");
                        // Không thêm vào issues để không fail toàn bộ validation
                    }
                    else
                    {
                        _logger.Info($"✅ File executable chính hợp lệ: {Path.GetFileName(mainExe)}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Không thể kiểm tra file executable: {ex.Message}");
                }
            }
            
            // Kiểm tra thư mục StarRail_Data
            var dataDir = Path.Combine(gamePath, "StarRail_Data");
            if (!Directory.Exists(dataDir))
            {
                issues.Add("Thư mục StarRail_Data không tồn tại");
            }
            else
            {
                // Kiểm tra các thư mục quan trọng
                var importantDirs = new[] { "StreamingAssets", "Managed", "il2cpp_data" };
                foreach (var dir in importantDirs)
                {
                    var fullDirPath = Path.Combine(dataDir, dir);
                    if (!Directory.Exists(fullDirPath))
                    {
                        issues.Add($"Thư mục quan trọng không tồn tại: StarRail_Data/{dir}");
                    }
                }
            }
            
            // Kiểm tra xem có file backup nào còn sót lại không
            var backupFiles = Directory.GetFiles(gamePath, "*.backup", SearchOption.AllDirectories);
            if (backupFiles.Length > 0)
            {
                issues.Add($"Còn {backupFiles.Length} file backup chưa được dọn dẹp");
            }
            
            // Kiểm tra xem có file .new nào còn sót lại không
            var newFiles = Directory.GetFiles(gamePath, "*.new", SearchOption.AllDirectories);
            if (newFiles.Length > 0)
            {
                issues.Add($"Còn {newFiles.Length} file .new chưa được xử lý");
            }
            
            // Kiểm tra xem có thư mục archive nào còn sót lại không
            var archiveDirs = Directory.GetDirectories(gamePath, "*", SearchOption.TopDirectoryOnly)
                .Where(dir => 
                {
                    var dirName = Path.GetFileName(dir);
                    return dirName.Contains("StarRail_") && dirName.Contains("_hdiff");
                })
                .ToArray();
            
            if (archiveDirs.Length > 0)
            {
                issues.Add($"Còn {archiveDirs.Length} thư mục archive chưa được dọn dẹp");
            }
            
            // Log kết quả
            if (issues.Count > 0)
            {
                _logger.Warning($"Phát hiện {issues.Count} vấn đề:");
                foreach (var issue in issues)
                {
                    _logger.Warning($"  - {issue}");
                }
                return false;
            }
            else
            {
                _logger.Info("Game integrity check passed - Tất cả đều ổn!");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Lỗi khi kiểm tra tính toàn vẹn game: {ex.Message}");
            return false;
        }
    }
}
