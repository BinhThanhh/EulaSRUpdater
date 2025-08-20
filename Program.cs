using EulaSR;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var updater = new GameUpdater();

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
    
    if (confirm == "y" || confirm == "yes")
    {
        Console.WriteLine();
        await updater.UpdateGameAsync(gamePath, hdiffPaths.ToArray());
    }
    else if (confirm == "n" || confirm == "no")
    {
        Console.WriteLine("Đã hủy cập nhật.");
    }
    else
    {
        await updater.UpdateGameAsync(gamePath, hdiffPaths.ToArray());
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
        await updater.UpdateGameFromUpdateFileAsync(gamePath, updateFilePath);
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
