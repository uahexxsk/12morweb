# LuaCrackVN + 12hub VM — Railway + Google/Discord + Pastefy

## Có gì trong bản này?

- Engine obfuscate Lua 5.1 của 12hub chạy phía server qua .NET 8 CLI.
- Đăng nhập OAuth bằng Google hoặc Discord thông qua Supabase Auth.
- Supabase lưu session phía client với `persistSession` và `autoRefreshToken` để tự khôi phục/refresh phiên đăng nhập.
- Tạo project: tải `.lua`/`.txt` hoặc dán source, tạo paste **UNLISTED** trên Pastefy, đọc raw source từ paste, chạy 12hub VM, rồi lưu payload và tạo loader riêng.
- Hai định dạng loadstring:
  - `https://TEN-MIEN/files/v1/loaders/SLUG.lua`
  - `https://TEN-MIEN/files/v4/loaders/SLUG.lua`
- Xoá script sẽ vô hiệu hoá loader trước, gọi Pastefy API để xoá paste, rồi xoá bản ghi local. Nếu Pastefy tạm lỗi, thao tác báo lỗi và không giả vờ đã xoá paste.
- `/admin` không có giao diện quản trị công khai; các hàm quản trị cũ vẫn yêu cầu kiểm tra quyền ở server.

## 1. Deploy lên Railway

1. Giải nén ZIP, đưa toàn bộ nội dung thư mục `luacrackvn-source` lên GitHub repository của bạn.
2. Railway → **New Project** → **Deploy from GitHub Repo** → chọn repository.
3. Để Railway build bằng `Dockerfile` đi kèm, không ép dùng Node/Nixpacks. Docker build cần .NET 8, Node 22, Lua 5.1 và LuaJIT.
4. Cấu hình các biến môi trường ở phần dưới. Các biến `VITE_*` được nhúng vào web lúc build, nên sau khi đổi chúng hãy **redeploy/rebuild**.
5. Chạy toàn bộ SQL migration trong `supabase/migrations/` theo thứ tự tên file trong Supabase SQL Editor. Phải chạy migration mới `20261017000000_projects_oauth_pastefy.sql` để thêm bảng `projects` và metadata liên kết Pastefy.

## 2. Biến Railway

Cấu hình các biến sau trong Railway → Service → Variables. Dùng giá trị từ **chính Supabase project của bạn**:

| Variable | Nội dung |
|---|---|
| `SUPABASE_URL` | Project URL của Supabase |
| `SUPABASE_PUBLISHABLE_KEY` | Publishable/anon key; dùng để xác minh Supabase Auth phía server |
| `SUPABASE_SERVICE_ROLE_KEY` | Service-role/secret key chỉ dùng phía server; tuyệt đối không dùng tiền tố `VITE_` |
| `VITE_SUPABASE_URL` | Bằng `SUPABASE_URL`; được dùng trong frontend OAuth |
| `VITE_SUPABASE_PUBLISHABLE_KEY` | Bằng `SUPABASE_PUBLISHABLE_KEY`; là key công khai, không phải service-role key |
| `PASTEFY_API_TOKEN` | API token từ https://pastefy.app/apikeys; cần để tạo và xoá paste |
| `OBF_XOR_KEY` | Chuỗi bí mật ngẫu nhiên ít nhất 32 ký tự |
| `SESSION_SECRET` | Chuỗi bí mật ngẫu nhiên mạnh (ít nhất 32 ký tự) |
| `HUB12_BUILD_TIMEOUT_MS` | Tuỳ chọn; mặc định `240000` ms |
| `HUB12_MAX_CONCURRENT_JOBS` | Tuỳ chọn; mặc định `2` |

Không đưa `SUPABASE_SERVICE_ROLE_KEY`, `PASTEFY_API_TOKEN`, `OBF_XOR_KEY` hay `SESSION_SECRET` vào bất kỳ biến `VITE_*` nào hoặc vào mã frontend.

## 3. Bật đăng nhập Google

1. Trong Google Cloud Console, tạo OAuth Client ID kiểu **Web application**.
2. Trong **Authorized redirect URIs**, thêm callback URL được hiển thị trong Supabase → **Authentication → Sign In / Providers → Google**. URL thường có dạng `https://<project-ref>.supabase.co/auth/v1/callback`; hãy sao chép chính xác URL từ Supabase.
3. Trong Supabase → **Authentication → Sign In / Providers → Google**, bật provider và lưu Google Client ID/Secret.

## 4. Bật đăng nhập Discord

1. Mở https://discord.com/developers/applications và tạo application.
2. Trong OAuth2, thêm redirect URI là callback chính xác được Supabase hiển thị tại **Authentication → Sign In / Providers → Discord** (thường `https://<project-ref>.supabase.co/auth/v1/callback`).
3. Sao chép Client ID và Client Secret vào provider Discord trong Supabase rồi bật provider.

## 5. Cho phép callback về domain Railway

Trong Supabase → **Authentication → URL Configuration**:

- Đặt **Site URL** thành domain public Railway của bạn, ví dụ `https://your-app.up.railway.app`.
- Thêm domain Railway chính xác vào **Redirect URLs** (ví dụ `https://your-app.up.railway.app/**`).
- Nếu dùng custom domain thì thêm cả custom domain vào danh sách cho phép.

Supabase OAuth cần cả cấu hình provider lẫn allow-list của URL chuyển hướng; chỉ thêm nút đăng nhập vào web là chưa đủ.

## 6. Cách sử dụng

1. Mở domain Railway rồi đăng nhập Google hoặc Discord.
2. Đặt tên project; upload `.lua`/`.txt` hoặc dán source Lua 5.1.
3. Chọn profile `Random`, `Lite`, `Balanced`, `Strong`, `Max` hoặc `Ultra`, rồi bấm **Tạo project + loader**.
4. Sau khi hoàn tất, web hiển thị loadstring v1 và v4 để copy, đồng thời lưu link Pastefy trong project.
5. Xoá script bằng nút **Xoá** để vô hiệu hoá loader và xoá paste thông qua Pastefy API.

Dạng loadstring mà web tạo:

```lua
loadstring(game:HttpGet("https://TEN-MIEN/files/v4/loaders/SLUG.lua"))()
```

Web dùng domain hiện tại khi tạo chuỗi loadstring, nên không cần sửa URL bằng tay.

## 7. Kiểm tra sau deploy

- Nút Google/Discord mở đúng trang OAuth; sau callback, dashboard vẫn đăng nhập khi reload trang.
- Tạo project test với một file Lua nhỏ.
- Kiểm tra project có Pastefy URL và hai loadstring v1/v4.
- Đảm bảo request tới slug hiện hành trả về payload; xoá project test rồi kiểm tra slug đó không còn phục vụ script.
- Nếu báo thiếu `PASTEFY_API_TOKEN`, thêm token ở Railway rồi redeploy.
- Nếu báo thiếu `SUPABASE_PUBLISHABLE_KEY`, kiểm tra biến server và `VITE_SUPABASE_PUBLISHABLE_KEY` cho build frontend.
- Nếu OAuth không trở về domain Railway, kiểm tra Supabase **URL Configuration** và callback URI ở provider.
- Nếu lỗi `12hub.dll`, xác nhận Railway đã dùng Dockerfile của repository và build stage engine hoàn tất.

## Lưu ý

Engine gốc dùng compiler Lua 5.1. Các extension riêng Luau/Roblox như type annotations, `continue`, `+=` và một số cú pháp không chuẩn Lua 5.1 có thể không compile. Giới hạn input là 5 MiB. Pastefy là một dịch vụ bên thứ ba; việc tạo/xoá paste phụ thuộc vào tài khoản và API token của bạn.
