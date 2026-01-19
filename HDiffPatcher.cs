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
        _logger = logger ?? Logger.ConsoleOnly();

        _hPatchExecutable = FindExecutable("hpatchz");
        _hDiffExecutable = FindExecutable("hdiffz");

        if (string.IsNullOrEmpty(_hPatchExecutable) && string.IsNullOrEmpty(_hDiffExecutable))
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            throw new FileNotFoundException($"hpatchz.exe or hdiffz.exe not found. Please place the files in:\n" +
                $"- {baseDir}\n" +
                $"- {Path.Combine(baseDir, "tools")}\n" +
                $"- {Environment.CurrentDirectory}\n" +
                $"Or add to PATH.");
        }
    }

    public async Task<bool> ApplyPatchAsync(string gamePath, string hdiffArchivePath, IProgress<string>? progress = null, string? password = null)
    {
        try
        {
            progress?.Report("Starting game update...");

            if (!Directory.Exists(gamePath))
            {
                throw new DirectoryNotFoundException($"Game folder does not exist: {gamePath}");
            }

            if (!File.Exists(hdiffArchivePath))
            {
                throw new FileNotFoundException($"HDiff file does not exist: {hdiffArchivePath}");
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "EulaSR_HDiff_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            try
            {
                progress?.Report("Extracting hdiff file...");
                await ExtractHDiffArchiveAsync(hdiffArchivePath, tempDir, password);

                progress?.Report("Processing updates...");
                await ProcessDeleteFilesAsync(gamePath, tempDir);

                try
                {
                    await ApplyAllPatchesAsync(gamePath, tempDir, progress);
                }
                catch (Exception patchEx)
                {
                    progress?.Report("Error occurred, rolling back...");

                    try
                    {
                        await RollbackChangesAsync(gamePath);
                        progress?.Report("Rollback successful.");
                    }
                    catch (Exception rollbackEx)
                    {
                        progress?.Report("Rollback failed. Check backup files.");
                    }

                    throw;
                }

                progress?.Report("Cleaning up temp files...");
                await CleanupTempFilesAsync(gamePath);

                progress?.Report("Update completed!");
                _logger.Info("Game update successful!");

                return true;
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"Error: {ex.Message}");
            return false;
        }
    }

    private async Task ExtractHDiffArchiveAsync(string archivePath, string extractPath, string? password = null)
    {
        var sevenZipPath = Find7ZipExecutable();
        var passwordArg = !string.IsNullOrEmpty(password) ? $" -p\"{password}\"" : "";
        var arguments = $"x \"{archivePath}\" -o\"{extractPath}\" -y -mmt{passwordArg}";

        var startInfo = new ProcessStartInfo
        {
            FileName = sevenZipPath,
            Arguments = arguments,
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
            if (error.Contains("Wrong password") || error.Contains("Cannot open encrypted archive") ||
                error.Contains("Data error") || output.Contains("Enter password"))
            {
                throw new UnauthorizedAccessException("7z file is encrypted. Need correct password to extract.");
            }

            throw new InvalidOperationException($"7-Zip extraction failed: {error}");
        }
    }

    private async Task ProcessDeleteFilesAsync(string gamePath, string tempDir)
    {
        var deleteFiles = Directory.GetFiles(tempDir, "deletefiles.txt", SearchOption.AllDirectories);
        var allFilesToDelete = new HashSet<string>();

        foreach (var deleteFile in deleteFiles)
        {
            var lines = await File.ReadAllLinesAsync(deleteFile);
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (!string.IsNullOrEmpty(trimmedLine) && !trimmedLine.StartsWith("#"))
                {
                    var normalizedPath = trimmedLine.Replace('/', Path.DirectorySeparatorChar);
                    allFilesToDelete.Add(normalizedPath);
                }
            }
        }

        if (allFilesToDelete.Count > 0)
        {
            foreach (var fileToDelete in allFilesToDelete)
            {
                var fullPath = Path.Combine(gamePath, fileToDelete);

                try
                {
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                    }
                    else if (Directory.Exists(fullPath))
                    {
                        Directory.Delete(fullPath, true);
                    }
                }
                catch { }
            }
        }
    }

    private async Task ApplyAllPatchesAsync(string gamePath, string tempDir, IProgress<string>? progress)
    {
        var hdiffFiles = Directory.EnumerateFiles(tempDir, "*.hdiff", SearchOption.AllDirectories).ToArray();
        var newFiles = Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".hdiff", StringComparison.OrdinalIgnoreCase) &&
                       !Path.GetFileName(f).Equals("deletefiles.txt", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var totalFiles = hdiffFiles.Length + newFiles.Length;

        if (totalFiles == 0)
        {
            return;
        }

        int currentIndex = 0;
        int skippedCount = 0;
        var lockObj = new object();

        var maxDegreeOfParallelism = Environment.ProcessorCount;
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism };

        await Parallel.ForEachAsync(hdiffFiles, parallelOptions, async (hdiffFile, ct) =>
        {
            var relativePath = Path.GetRelativePath(tempDir, hdiffFile);
            var originalFileName = Path.GetFileNameWithoutExtension(hdiffFile);
            var relativeDir = Path.GetDirectoryName(relativePath) ?? "";
            var targetFilePath = Path.Combine(gamePath, relativeDir, originalFileName);

            int localIndex;
            lock (lockObj)
            {
                currentIndex++;
                localIndex = currentIndex;
            }

            var percentage = (int)((double)localIndex / totalFiles * 100);
            progress?.Report($"[{percentage}%] Patching {localIndex}/{totalFiles}: {originalFileName}");

            try
            {
                await ApplySinglePatchAsync(targetFilePath, hdiffFile);
            }
            catch (Exception ex)
            {
                lock (lockObj)
                {
                    skippedCount++;
                }
            }
        });

        await Parallel.ForEachAsync(newFiles, parallelOptions, async (newFile, ct) =>
        {
            var relativePath = Path.GetRelativePath(tempDir, newFile);
            var fileName = Path.GetFileName(newFile);
            var relativeDir = Path.GetDirectoryName(relativePath) ?? "";
            var targetFilePath = Path.Combine(gamePath, relativeDir, fileName);

            int localIndex;
            lock (lockObj)
            {
                currentIndex++;
                localIndex = currentIndex;
            }

            var percentage = (int)((double)localIndex / totalFiles * 100);
            progress?.Report($"[{percentage}%] Copying {localIndex}/{totalFiles}: {fileName}");

            try
            {
                await CopyNewFileAsync(newFile, targetFilePath);
            }
            catch (Exception ex)
            {
                _logger.Warning($"⚠️ Failed to copy {fileName}: {ex.Message}");
                lock (lockObj)
                {
                    skippedCount++;
                }
            }
        });
    }

    private async Task ApplySinglePatchAsync(string targetFile, string patchFile)
    {
        var fileName = Path.GetFileName(targetFile);

        if (!File.Exists(targetFile))
        {
            return;
        }

        var backupFile = targetFile + ".backup";
        File.Copy(targetFile, backupFile, true);

        try
        {
            var tempOutputFile = targetFile + ".new";
            bool patchSuccess = false;

            if (!string.IsNullOrEmpty(_hPatchExecutable))
            {
                patchSuccess = await TryApplyPatchWithTool(_hPatchExecutable, "hpatchz", targetFile, patchFile, tempOutputFile);
            }
            if (!patchSuccess && !string.IsNullOrEmpty(_hDiffExecutable))
            {
                patchSuccess = await TryApplyPatchWithTool(_hDiffExecutable, "hdiffz", targetFile, patchFile, tempOutputFile);
            }

            if (!patchSuccess || !File.Exists(tempOutputFile))
            {
                throw new InvalidOperationException($"Patch failed for {fileName}");
                File.Move(tempOutputFile, targetFile, true);
                File.Delete(backupFile);
            }
        }
        catch (Exception)
        {
            if (File.Exists(backupFile))
            {
                File.Move(backupFile, targetFile, true);
            }
            throw;
        }
    }

    private async Task CleanupTempFilesAsync(string gamePath)
    {
        try
        {
            var allFilesToDelete = new List<string>();

            var tempFilesToDelete = new[] { "deletefiles.txt" };
            foreach (var tempFileName in tempFilesToDelete)
            {
                allFilesToDelete.AddRange(Directory.EnumerateFiles(gamePath, tempFileName, SearchOption.AllDirectories));
            }

            allFilesToDelete.AddRange(Directory.EnumerateFiles(gamePath, "*.backup", SearchOption.AllDirectories));
            allFilesToDelete.AddRange(Directory.EnumerateFiles(gamePath, "*.new", SearchOption.AllDirectories));

            await Parallel.ForEachAsync(allFilesToDelete, async (fileToDelete, ct) =>
            {
                try
                {
                    await Task.Run(() => File.Delete(fileToDelete), ct);
                }
                catch { }
            });
        }
        catch { }
    }

    private async Task CopyNewFileAsync(string sourceFile, string targetFile)
    {
        var targetDir = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        const int bufferSize = 1024 * 1024; // 1MB buffer
        using var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        using var targetStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
        await sourceStream.CopyToAsync(targetStream, bufferSize);
    }

    private string FindExecutable(string toolName)
    {
        var possibleNames = new[] { $"{toolName}.exe", toolName };
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var searchPaths = new[]
        {
            baseDir,
            Path.Combine(baseDir, "tools"),
            Environment.CurrentDirectory,
            Path.Combine(Environment.CurrentDirectory, "tools")
        };
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
        return "";
    }

    private string Find7ZipExecutable()
    {
        var possibleNames = new[] { "7z.exe", "7za.exe", "7z" };
        var possiblePaths = new[]
        {
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe"
        };
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }
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

    private async Task<bool> TryApplyPatchWithTool(string toolPath, string toolName, string targetFile, string patchFile, string outputFile)
    {
        try
        {
            var arguments = $"\"{targetFile}\" \"{patchFile}\" \"{outputFile}\"";
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

            if (process.ExitCode == 0 && File.Exists(outputFile))
            {
                return true;
            }
            if (toolName.Equals("hdiffz", StringComparison.OrdinalIgnoreCase))
            {
                var altArguments = $"-f \"{targetFile}\" \"{patchFile}\" \"{outputFile}\"";
                startInfo.Arguments = altArguments;

                using var process2 = new Process { StartInfo = startInfo };
                process2.Start();

                var output2 = await process2.StandardOutput.ReadToEndAsync();
                var error2 = await process2.StandardError.ReadToEndAsync();
                await process2.WaitForExitAsync();

                if (process2.ExitCode == 0 && File.Exists(outputFile))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    private async Task RollbackChangesAsync(string gamePath)
    {
        try
        {
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
                }
                catch { }
            }

            var newFiles = Directory.GetFiles(gamePath, "*.new", SearchOption.AllDirectories);
            foreach (var newFile in newFiles)
            {
                try
                {
                    File.Delete(newFile);
                }
                catch { }
            }
        }
        catch { }
    }

    private async Task<bool> ValidateGameIntegrityAsync(string gamePath)
    {
        try
        {
            _logger.Info("Starting game integrity check...");
            var issues = new List<string>();
            var mainExe = Directory.GetFiles(gamePath, "*.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => Path.GetFileName(f).ToLower().Contains("starrail"));
            if (mainExe == null)
            {
                issues.Add("Main game executable not found");
            }
            else if (!File.Exists(mainExe))
            {
                issues.Add($"Main executable does not exist: {Path.GetFileName(mainExe)}");
            }
            else
            {
                _logger.Info($"✅ Main executable valid: {Path.GetFileName(mainExe)}");
            }

            var dataDir = Path.Combine(gamePath, "StarRail_Data");
            if (!Directory.Exists(dataDir))
            {
                issues.Add("StarRail_Data directory does not exist");
            }
            else
            {
                var importantDirs = new[] { "StreamingAssets", "Managed", "il2cpp_data" };
                foreach (var dir in importantDirs)
                {
                    var fullDirPath = Path.Combine(dataDir, dir);
                    if (!Directory.Exists(fullDirPath))
                    {
                        issues.Add($"Important directory does not exist: StarRail_Data/{dir}");
                    }
                }
            }
            var backupFiles = Directory.GetFiles(gamePath, "*.backup", SearchOption.AllDirectories);
            if (backupFiles.Length > 0)
            {
                issues.Add($"{backupFiles.Length} backup files not cleaned up");
            }
            var newFiles = Directory.GetFiles(gamePath, "*.new", SearchOption.AllDirectories);
            if (newFiles.Length > 0)
            {
                issues.Add($"{newFiles.Length} .new files not processed");
            }
            if (issues.Count > 0)
            {
                _logger.Warning($"Detected {issues.Count} issues:");
                foreach (var issue in issues)
                {
                    _logger.Warning($"  - {issue}");
                }
                return false;
            }
            else
            {
                _logger.Info("Game integrity check passed - Everything is fine!");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Error checking game integrity: {ex.Message}");
            return false;
        }
    }
}
