# shared/navigation

Menu là dữ liệu, và quyết định điều hướng là hàm thuần.

| File | Việc |
| --- | --- |
| `nav.ts` | Hình dạng menu + luật lọc theo quyền |
| `guard.ts` | Quyết định cho-qua / redirect / đợi, và chặn open redirect |

Cả hai **không import `vue-router`**. Chúng nhận dữ liệu vào, trả kết quả ra. `app/router/index.ts` là chỗ duy nhất biết tới router.

## Menu: hình dạng ở base, dữ liệu ở project

```
shared/navigation/nav.ts   ← NavItem, visibleNav()  (đi theo base)
app/nav.config.ts          ← menu thật               (mỗi khách một bản)
```

`nav.config.ts` ở `app/` vì nó tham chiếu route name của các feature, và `shared/` không được import `features/`.

`routeName` là **tên route**, không phải path — đổi path không phải sửa menu.

### Ba luật lọc

1. Link thiếu quyền **bị bỏ hẳn**, không hiện dạng disabled. Một mục xám bấm không được chỉ nói với người dùng rằng có thứ họ không được xem.
2. **Group rỗng bị bỏ.** Đây là lỗi hay gặp nhất: lọc từng link nhưng vẫn hiện tiêu đề group, để lại một mục bấm vào không ra gì.
3. Đệ quy — group lồng group cũng rỗng-thì-bỏ, tính từ trong ra ngoài.

`can()` fail closed khi `permissions === null` (chưa tải xong user), nên trong lúc bootstrap menu rỗng chứ không hiện đầy đủ rồi co lại. Nhấp nháy một menu đầy đủ rồi biến mất tệ hơn menu rỗng.

Khai cả `permission` và `anyPermission` → phải thoả **cả hai**. Đó là ý "bắt buộc có X, và có một trong Y/Z".

### `routeName` là chuỗi nên gõ sai được

`unknownRouteNames(navConfig, router.getRoutes())` chạy ở dev lúc boot. Không có nó thì một mục menu bấm vào không đi đâu và **không có gì báo**.

## Guard: bốn luật, mỗi luật sửa một lỗi thật

`resolveNavigation()` là hàm thuần, có 20 test. `router.beforeEach` chỉ còn ba dòng: đọc state, gọi hàm, thi hành kết quả.

Lý do tách: logic viết trực tiếp trong `beforeEach` cần dựng cả router, app và store mới test được — nên trên thực tế nó **không được test**. Và guard là chỗ một lỗi có nghĩa là người không quyền vào được trang.

| # | Luật | Không có thì |
| --- | --- | --- |
| 1 | **Mặc định CẦN đăng nhập.** Route phải khai `public: true` để mở | Bản cũ làm ngược và có 7/11 route không gate. Quên khai `meta` phải là lỗi "không vào được", không phải "ai cũng vào được" |
| 2 | Thiếu quyền → **403**, không phải login | Đẩy về login khi đã đăng nhập tạo vòng lặp: đăng nhập lại → vẫn thiếu quyền → lại về login. Người dùng nghĩ mật khẩu sai |
| 3 | Chưa biết quyền → **`wait`**, không đoán | Cho qua thì UI nhấp nháy nội dung rồi bị đẩy ra; chặn thẳng thì F5 trên trang sâu luôn ném về login dù có quyền |
| 4 | Đang đăng nhập → không vào được trang login | Bấm Back sau khi đăng nhập là về form login, và submit form đó tạo phiên thứ hai |

Luật 3 áp dụng cho **mọi route bảo vệ** (yêu cầu đăng nhập). Khi có phiên nhưng chưa nạp thông tin người dùng (`!subjectReady`), guard trả về `wait` để gọi `ensureLoaded()`. Điều này bảo đảm `AppShell` có đầy đủ danh tính và quyền hạn để render cây điều hướng chính xác, tránh lỗi F5 trên route mặc định (`/`) khiến toàn bộ menu biến mất và chỉ còn mỗi Tổng quan.

`wait` tối đa xảy ra một lần: sau `ensureLoaded()` thì `subjectReady` là `true` kể cả khi lỗi, nên không có vòng lặp.

## `safeRedirectTarget` — chặn open redirect

`?redirect=...` sau khi đăng nhập là một lỗ hổng thật nếu không lọc: link trông như của hệ thống, đăng nhập xong bị đẩy sang site khác.

Bốn thứ bị từ chối:

| Dạng | Vì sao |
| --- | --- |
| `https://host` | URL tuyệt đối |
| `//host` | URL tương đối protocol — browser coi là tên miền ngoài |
| `/\host` | một số browser chuẩn hoá `\` thành `/` |
| `/javascript:…` | scheme lồng trong path |

Chỉ nhận chuỗi bắt đầu bằng **đúng một** dấu `/`.

## Ẩn menu không phải là kiểm quyền

Hai cơ chế độc lập, phải khai **cả hai**:

- `nav.config.ts` → mục có hiện trong sidebar hay không
- `meta.access` của route → có vào được hay không

Người dùng vẫn gõ được URL trực tiếp. Chỉ ẩn menu là bảo vệ bằng cách giấu.
