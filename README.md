# EULA SR Game Updater

Công cụ cập nhật Honkai: Star Rail bằng file hdiff.

## Yêu cầu hệ thống

- Windows 10/11
- .NET 9.0 Runtime
- 7-Zip (tải từ [7-zip.org](https://www.7-zip.org/))

## Cài đặt

1. **Cài đặt 7-Zip:**
   - Tải từ: https://www.7-zip.org/
   - Cài đặt bình thường (sẽ tự động thêm vào PATH)

2. **Biên dịch:**
   ```bash
   dotnet build --configuration Release
   ```

3. **Chạy:**
   ```bash
   dotnet run
   ```
   
   Hoặc sử dụng file batch:
   ```bash
   run.bat
   ```

## Xử lý lỗi

|        Lỗi              |           Nguyên nhân          |         Giải pháp                |
|-------------------------|--------------------------------|----------------------------------|
| Insufficient disk space | Không đủ dung lượng            | Giải phóng dung lượng ổ đĩa      |
| Permission denied       | Thiếu quyền ghi                | Chạy với quyền Administrator     |
| File not found          | Đường dẫn sai                  | Kiểm tra đường dẫn game và hdiff |
