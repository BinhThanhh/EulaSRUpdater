# EULA SR Game Updater

Công cụ cập nhật Honkai: Star Rail bằng file hdiff.

## Yêu cầu hệ thống

- Windows 10/11
- .NET 9.0 Runtime
- HDiffZ executable (tải từ [GitHub](https://github.com/sisong/HDiffPatch))
- 7-Zip (tải từ [7-zip.org](https://www.7-zip.org/))

## Cài đặt

1. **Tải HDiffZ:**
   - Tải từ: https://github.com/sisong/HDiffPatch/releases
   - Giải nén và đặt `hdiffz.exe` vào PATH hoặc cùng thư mục với EulaSR.exe

2. **Cài đặt 7-Zip:**
   - Tải từ: https://www.7-zip.org/
   - Cài đặt bình thường (sẽ tự động thêm vào PATH)

3. **Biên dịch:**
   ```bash
   dotnet build --configuration Release
   ```

4. **Chạy:**
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
| HDiffZ not found        | Thiếu HDiffZ executable        | Cài đặt HDiffZ và đặt vào PATH   |
| Insufficient disk space | Không đủ dung lượng            | Giải phóng dung lượng ổ đĩa      |
| Permission denied       | Thiếu quyền ghi                | Chạy với quyền Administrator     |
| File not found          | Đường dẫn sai                  | Kiểm tra đường dẫn game và hdiff |

## Hỗ trợ

Nếu gặp vấn đề:
1. Kiểm tra file log trong thư mục `logs/`
2. Đảm bảo HDiffZ đã được cài đặt đúng
3. Chạy với quyền Administrator
4. Kiểm tra dung lượng ổ đĩa
