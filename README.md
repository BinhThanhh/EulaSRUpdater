# EULA SR Game Updater

Tool update Honkai Star Rail using hdiff.

## Requirements

- Windows 10/11
- .NET 9.0 Runtime
- 7-Zip (download from [7-zip.org](https://www.7-zip.org/))

## Installation

1. **Install 7-Zip:**
   - Download from: https://www.7-zip.org/
   - Install normally (will automatically add to PATH)

2. **Build:**

   ```bash
   dotnet build --configuration Release
   ```

3. **Run:**

   ```bash
   dotnet run
   ```

   Or use batch file:

   ```bash
   run.bat
   ```

## Error handling

| Error                   | Reason                   | Solution                       |
| ----------------------- | ------------------------ | ------------------------------ |
| Insufficient disk space | Not enough disk space    | Free up disk space             |
| Permission denied       | Insufficient permissions | Run as Administrator           |
| File not found          | Path is wrong            | Check game path and hdiff path |
