# shared/ui

Primitive giao diện. Đây là **nơi duy nhất** được viết style — Base architecture D2.

Không có `index.ts`. Import trực tiếp file: `import Button from '#/shared/ui/Button.vue'`.

## Vì sao không có barrel ở đây — đã đo, không phải quy ước

Câu cũ ở chỗ này viết "barrel ở đây là ceremony và làm Vite tree-shake kém" mà **không nêu cơ chế**. Một luật không có lý do là luật sẽ bị người sau xoá, nên đây là số đo thật.

**Phép thử**: tạo `shared/ui/index.ts` named re-export 5 primitive, đổi 3 importer sang dùng nó, `vite build`, so với baseline.

| | Không barrel | Có barrel | Δ |
| --- | --- | --- | --- |
| module | 185 | **191** | **+6** |
| tổng JS | 229,02 kB | 231,30 kB | +2,28 kB |
| CSS theo route | `Button.css` 2,08 kB — **chỉ** route dùng Button phải tải | `ui.css` 3,53 kB — **mọi** route dùng bất kỳ primitive đều phải tải | xấu hơn |

Lúc đó `Modal.vue` có **0 importer**. Nó bị kéo vào chỉ vì có tên trong barrel.

**Cơ chế**: SFC có block `<style>`, và với Rollup thì import CSS là **side effect** — nên `export { default as X } from './X.vue'` không xoá được, dù không ai dùng `X`.

**Hệ quả về `core/`**: barrel ở `core/<slice>/index.ts` re-export toàn `.ts` **thuần**, và cái đó tree-shake được. Cộng với việc `core/` là biên trích xuất package (D3, §10.4), nên nó có barrel là hợp lệ. Bất đối xứng giữa hai thư mục **không phải bất nhất** — nó là hai loại nội dung khác nhau.

⚠️ Nửa **chưa đo**: tôi chưa build một ca đối chứng cho barrel `.ts` thuần để chứng minh nó *thật sự* tree-shake được. Điều đó suy ra từ cơ chế side-effect, và `package.json` **không** khai `"sideEffects": false` nên nó phụ thuộc phân tích tĩnh của Rollup. Muốn nâng lên `[ĐO]` thì phải build thêm một ca.

Gate `vue-barrel` trong `scripts/check-architecture.mjs` chặn việc này ở **mọi tầng**, phát biểu theo `.vue` chứ không theo tầng — phát biểu theo tầng sẽ báo oan `mocks/handlers/index.ts`.

## Rule of two — thấy được ngay trong folder này

```
shared/ui/
  Button.vue          ← file phẳng: chỉ có một file cho chủ đề này
  Spinner.vue         ← file phẳng
  Input.vue           ← file phẳng
  DateInput.vue       ← file phẳng: ô nhập ngày chuẩn hoá HTML5 có nhãn prefix và icon lịch
  FormField.vue       ← file phẳng
  Badge.vue           ← file phẳng: nhãn mức ngữ nghĩa, không nền
  Modal/              ← folder: BỐN file nguồn cùng chủ đề
    Modal.vue
    Modal.css
    useFocusTrap.ts
    useScrollLock.ts
  DataTable/          ← folder: SÁU file nguồn, CSS dùng chung cho hai component
    DataTable.vue
    DataTable.css
    DataTablePagination.vue
    table-types.ts
    table-types.guard.ts   ← negative control cho 5 guard compile-time
    use-table-data.ts      ← ánh xạ trạng thái query → TableData (hàm thuần)
  no-business-logic.test.ts  ← bất biến I2: shared/ui không chứa token nghiệp vụ
```

Hai cây trên đếm **file nguồn**, không đếm file test. `Modal/` có 4 nguồn + 2 test; `DataTable/` có 6 nguồn + 3 test. Câu cũ ở đây ghi "BA file" rồi liệt bốn — đếm sai và không nói đơn vị.

`table-types.guard.ts` không export gì có ích và không ai import nó. Nó dùng `@ts-expect-error` để nghiệm rằng bốn claim compile-time **thật sự** đỏ, và nó tự nghiệm hai chiều: guard bị nới thì directive thành dư và `vue-tsc` báo `Unused '@ts-expect-error' directive` ⇒ gate 1 đỏ.

`ARCHITECTURE.md` §2.3 vẽ `DataTable/` với bốn file gồm `useTableSort.ts`. Cây đó là **minh hoạ** một `DataTable` đã lớn, không phải mệnh lệnh khởi tạo — luật là "file phẳng cho tới khi có file thứ hai", và câu giải thích dùng thể điều kiện ("**khi** `DataTable` **có** 4 file thì…"). Sort không có ở đây vì **0/8** view cũ cần nó; dựng `useTableSort.ts` bây giờ là đúng thứ ceremony mà §2.3 cảnh báo.

`Badge.vue` là **file phẳng** — một component nhỏ, chưa có file thứ hai cùng chủ đề nên chưa thành folder. `Badge/Badge.vue` cho nó là ceremony.

Không `index.vue` (mọi tab trong editor tên giống nhau). Không `index.ts` trong folder component.

## Component

| Component | Việc |
| --- | --- |
| `Button.vue` | Nút. `type` mặc định `'button'`, `loading` tự kéo theo `disabled` + `aria-busy`, `white-space: nowrap` chống ngắt dòng |
| `Spinner.vue` | Chỉ báo tải, có nhãn cho screen reader |
| `Input.vue` | Ô nhập một dòng. Phần tử gốc **chính là** `<input>` |
| `FormField.vue` | Nhãn + gợi ý + lỗi, nối bằng `aria-describedby` / `aria-invalid` |
| `Badge.vue` | Nhãn mức ngữ nghĩa (`neutral`/`info`/`success`/`warning`/`danger`). Không nền, chữ + viền cùng màu; `srLabel` cho screen reader. Không nhận màu, không biết nghiệp vụ |
| `Modal/Modal.vue` | Dialog: focus trap, trả focus, Escape, chặn cuộn, `aria-modal` |
| `DangerConfirmDialog/DangerConfirmDialog.vue` | Khung xác nhận nguy hiểm chuẩn Datacake (WAI-ARIA `alertdialog`, Intentional Friction: so khớp chuỗi client-side, clipboard copy, disabled button, focus trap) |
| `DataTable/DataTable.vue` | Bảng. `colspan` suy ra, **5 trạng thái** loại trừ ở tầng type (`loading`/`paused`/`refetching`/`ready`/`error`), `scope`/`caption`/live region, render null-safe |
| `DataTable/DataTablePagination.vue` | Footer phân trang từ `Page<T>`. Sở hữu tính nhất quán của `Page` |
| `DataTable/PageSizeSelect.vue` | Ô chọn số dòng mỗi trang chuẩn Datacake (native `<select>` + SVG chevron `M6 8l4 4 4-4`, theme-aware, accessibility) |
| `DataTable/use-table-data.ts` | Hàm thuần: ánh xạ trạng thái `useQuery` → `TableData`. Nhận cấu trúc tối thiểu (`ReadRef`), KHÔNG import `@tanstack` (gate `ui-no-query-lib` giữ) |
| `PageContainer.vue` | Khung trang **trong `AppShell`**: bề rộng + căn giữa + khoảng cách dọc. Hai biến thể `default`/`prose`. KHÔNG khai `padding` — `.shell__main` là chủ của khoảng cách ngoài |
| `PageHeader.vue` | Đầu trang: `<h1>` + mô tả + slot `actions`. Chủ duy nhất của `<h1>` trong một trang nghiệp vụ |
| `Card.vue` | Bề mặt `--surface-panel` + viền + bo góc, chế độ `flush` bỏ padding. **Không được khai `overflow`** — gate `card-no-overflow` giữ |

### Vì sao KHÔNG có `Stack` và `Cluster` — đã cân nhắc và loại

Spec 007 đo được **8** chỗ dùng idiom `flex-direction: column` và **9** chỗ dùng `align-items: center`. Con số đó trông như đủ để trích một abstraction, và bản nháp đã định trích.

Bị loại sau red-team, hai lý do:

1. **Tập 9 chứa ca chỉ giống nhau bề mặt.** `Button` và `Badge` dùng `inline-flex` vì bản thân chúng là inline element, không vì chúng "là một cluster". Gom chúng vào một abstraction chung là abstraction sai.
2. **Phần lặp ở tầng page đã bị `PageContainer` hấp thu.** Bốn chỗ `flex-column` thuần còn lại nằm trong component (`FormField`, `AuditLogTable`) hoặc trong form (`LoginView`) — không phải chỗ mà "người viết page tiếp theo" phải chạm.

Một `Stack` với prop `gap`/`align`/`as`/`wrap` là mini-CSS-qua-prop, và ở tầng cell của bảng nó thành hàng trăm component instance chỉ để đặt `display: flex`. Ba dòng CSS tại chỗ rẻ hơn.

**Đừng "bổ sung cho đủ".** Nếu xuất hiện một ca mà ba dòng CSS đó thật sự là cùng một ý niệm gọi tên được, hãy trích lúc đó.

### Sáu component đã thiết kế nhưng CHƯA dựng

`Toolbar`, `IconButton`, `Breadcrumb`, `Select`, `Checkbox`, `Tabs`. Tất cả có **0 consumer** trong phạm vi spec 007 — không có row action, không có filter thật, không có UI đổi `pageSize`, không có chọn nhiều dòng, không nối tab vào router.

Dựng chúng bây giờ là thiết kế chữ ký cho một người dùng tưởng tượng. Đây là đúng luật đã dùng để loại `useTableSort.ts` (0/8 consumer), áp lại cho chính spec 007. Chúng được dựng ở spec của page cần chúng.

`Select`/`Checkbox`/`Tabs` khi dựng thì **bọc `reka-ui`** (đã có trong `dependencies`, chưa dùng), không tự viết — trừ khi có lý do tương đương lý do `Modal` không dùng `<dialog>`.

## Những quyết định dễ bị hỏi lại

### `Button` mặc định `type="button"`

HTML mặc định `<button>` trong `<form>` là `submit`. Một nút "Hủy" hoặc "Thêm dòng" trong form sẽ submit form đó, và **lỗi này im lặng**. Đảo mặc định là cách duy nhất chặn nó ở gốc.

`loading` tự kéo theo `disabled`: không có thì người dùng bấm lần hai trong lúc request đang bay và tạo hai bản ghi trùng. Idempotency ở backend là chuyện khác, không thay thế được cái này.

Cố tình **không** render thành `<a>`. Nút và link có hành vi bàn phím khác nhau (Space so với Enter); trộn hai thứ vào một component là để cả hai sai. Cần thì viết `LinkButton` riêng.

### `Input` không có prop `label` / `error`

Có `input`, `select`, `textarea` và control tự viết. Nhồi nhãn vào từng cái là nhân bản cùng một logic `aria-describedby` — và bản thứ ba sẽ sai. Nhãn và lỗi thuộc `FormField`.

Trạng thái sai lấy từ `aria-invalid`, không từ prop riêng:

```css
.input[aria-invalid='true'] { border-color: var(--status-danger); }
```

Một nguồn duy nhất thì viền đỏ và thứ screen reader nghe được **không lệch nhau được**.

### `FormField` dùng `aria-live`, không `role="alert"`

Vùng lỗi **luôn có trong DOM**. Nếu chỉ render khi có lỗi và gắn `role="alert"` thì lỗi validate trả về từ server lúc mở form bị đọc ngay khi vào trang, trước cả khi người dùng biết mình đang ở form nào. Vùng live có sẵn thì chỉ **thay đổi** mới được thông báo.

Dấu `*` là `aria-hidden`: `required` trên control đã được đọc là "bắt buộc"; không ẩn thì nghe thành "ngôi sao" giữa tên field.

### `Modal` không dùng `<dialog>` của HTML

`showModal()` là API bắt buộc gọi bằng lệnh, nên nó đánh nhau với `v-if`, và cách style backdrop khác nhau giữa các browser. Dựng tay thì kiểm soát được cả bảy dòng dưới đây:

| Thứ | Không có thì |
| --- | --- |
| Focus trap | Tab đi ra sau lớp phủ, tới nút không nhìn thấy |
| Trả focus khi đóng | Người dùng bàn phím phải Tab lại từ đầu trang |
| Escape để đóng | Không có đường thoát bằng bàn phím |
| `role="dialog"` + `aria-modal` | Screen reader vẫn đọc nội dung nằm dưới |
| `aria-labelledby` | Screen reader nói "dialog" mà không nói dialog gì |
| Chặn cuộn trang | Cuộn chuột cuộn trang phía sau |
| `Teleport` | `position: fixed` bị cắt bởi tổ tiên có `transform` |

`Modal` là thứ thay cho `confirm()` và `alert()` — cả hai bị ESLint chặn.

`persistent` để Escape và click ra ngoài **không** đóng: dùng cho form đã nhập dở, đóng ngoài ý muốn làm mất dữ liệu vừa gõ.

### `useFocusTrap` chỉ chặn Tab, không chặn `focusin`

Cách chắc hơn là nghe `focusin` ở document rồi kéo focus về. Cố tình không làm: Select / Combobox / Popover render nội dung ra ngoài cây DOM bằng portal, và kéo focus về sẽ giật focus khỏi chính cái dropdown vừa mở. Chặn Tab bao phủ đúng cái cần bao phủ — điều hướng bằng bàn phím — mà không đánh nhau với portal.

`getFocusable` lọc theo **thuộc tính** (`hidden`, `inert`, `disabled`, `aria-hidden`), không theo layout. `getClientRects().length` là cách hay gặp nhưng jsdom không tính layout nên nó trả rỗng cho mọi phần tử — và một focus trap không test được thì không biết là có chạy.

### `useScrollLock` đếm số overlay

`document.body` là toàn cục nhưng có thể có hai overlay lồng nhau. Bộ đếm ở **module scope**, chia sẻ giữa mọi instance. Đóng dialog trong không được mở cuộn lại khi dialog ngoài còn mở.

Bù bề rộng scrollbar khi ẩn nó, và **lưu rồi trả lại** giá trị cũ thay vì gán rỗng.

### `DataTable` — bốn quyết định, mỗi cái chặn một defect ĐO ĐƯỢC

Yêu cầu của nó trích từ **8 view đã bị xoá** (khôi phục ở commit `86e22f4`), không từ suy đoán. Số đo ở `specs/005-data-table/requirements.md` §1.

**`colspan` là `computed`, không phải hằng.** Bản cũ hard-code `colspan` ở **7 chỗ**; thêm hay bớt một cột là 7 chỗ sai âm thầm. Và tập cột phụ thuộc quyền — quyền đến từ store nên nó đổi **sau** lần render đầu.

**Trạng thái là một discriminated union, không phải các prop boolean rời.** `{ state: 'error' }` thiếu `message` là **lỗi compile**; `{ state: 'refetching', rows: [] }` cũng vậy vì nó đòi `readonly [T, ...T[]]`. Lý do có ràng buộc thứ hai: tab để lâu → refetch → cache đã bị tab khác xoá → `rows` rỗng trong lúc refetching → bảng cảnh báo hiện "không có cảnh báo nào" cho một khoảng **có** cảnh báo. Không log, không đỏ, hình ảnh hợp lệ.

**Trạng thái thứ năm `paused` (spec 006).** Mất mạng lúc **chưa từng** có dữ liệu (`isPaused && isPending`): `queryFn` không chạy nên không phải `loading`, cũng không có lỗi nên không phải `error`. `paused` không mang rows. Ai ánh xạ `useQuery` → `TableData` là **`use-table-data.ts`** (hàm thuần, 5 nhánh) — không để mỗi view tự viết luật đó rồi lệch nhau. Điểm quan trọng: **warm-paused** (mất mạng khi đang có dữ liệu) KHÔNG rơi vào `paused` mà giữ `ready`, nên mạng chập không xoá bảng đang hiển thị. Đó là lý do nhánh 1 gác `&& isPending`, không phải `isPaused` trần.

**Chỉ `null`/`undefined` ra ký hiệu thiếu.** `0`, `false`, `NaN` đi qua nguyên vẹn. Bản cũ viết `a.triggerValue ? fmt(a.triggerValue) : 'Mất tín hiệu'` và ứng suất **0 MPa** — một phép đo hợp lệ — hiện thành "Mất tín hiệu". Không dùng `||` hay `?:` trên chính giá trị ở bất kỳ đâu trong component.

**Có `format` bên cạnh slot, không phải chỉ có slot.** Cột ngày (**5/8** view) và cột số có đơn vị (**3/8**) nếu buộc phải dùng slot thì chúng nằm **ngoài** lá chắn ở trên — trong slot `value` là `unknown`. `format` đưa chúng về đường có guard: `null`/`undefined` không bao giờ vào `format`, nên `format` không phải tự kiểm và `.toFixed()` không throw trong render. `key` bị ràng buộc vào `keyof T` nên gõ sai là lỗi compile.

**`<thead>` không có nền, và không hover nền hàng.** Hai lý do đo được: theme sáng đặt `--surface-panel` và `--surface-overlay` **cùng `#ffffff`** nên nền header vô hình, mà cổng 9 chỉ đo fg-vs-bg nên nó vẫn xanh; và badge trạng thái có ở **8/8** bảng cũ, đặt nền mới dưới chúng tạo cặp `status-*` / `surface-overlay` mà `PAIRS` **không có**. Header phân biệt bằng viền dưới + chữ hoa + cỡ nhỏ. Kết quả: 0 token mới, 0 cặp mới.

Giới hạn phải biết: **slot là đường thoát và trong slot không có guard nào.** Ai viết `value ? fmt(value) : '…'` trong slot thì defect trên quay lại, và không type hay cổng nào chặn. `format` tồn tại để đường thoát đó không còn **bắt buộc**.

## Style

Mọi màu đi qua CSS variable của `app/styles/main.css`. Component **không hard-code màu** — đổi theme không phải sửa component.

Token trạng thái đặt tên theo **ý nghĩa**, không theo tên màu: `--status-success`, `--status-info`, `--status-warning`, `--status-danger`, cùng bốn biến `-soft`.
Riêng các chấm tròn trạng thái (indicator dot) sử dụng nhóm token chuyên biệt: `--status-success-dot` (#10b981 ở theme sáng), `--status-neutral-dot` (#9ca3af), `--status-warning-dot` (#f59e0b), `--status-danger-dot` (#ef4444). Việc tách token dot giúp các chấm đồ họa hiển thị màu sắc tươi sáng chuẩn Datacake mà không phá vỡ ngưỡng tương phản WCAG 4.5:1 của token text/viền (`--status-success` dùng #047d57 cho chữ). Mọi biểu diễn icon online/offline trên toàn hệ thống phải dùng chung component `StatusDot` hoặc `StatusIndicator`. Ánh xạ trạng thái nghiệp vụ vào bốn mức này thuộc tài liệu binding của từng dự án.

Hệ thống thống nhất sử dụng cặp trạng thái nghiệp vụ chuẩn **'Hoạt động'** và **'Tạm dừng'** (đồng bộ triệt để thay cho việc dùng lẫn lộn 'Đang hoạt động', 'Đang quan trắc' hay 'Đang đo'). Toàn bộ nhãn trạng thái và chuyển đổi song ngữ VI / EN được quản trị tập trung tại `src/shared/i18n/status.ts` (`COMMON_STATUSES`, `translateStatus`, `useStatusTranslation`, `getStandardStatusOptions`). Component `StatusIndicator` hỗ trợ thuộc tính `autoLabel` tự động dịch nhãn theo trạng thái hiện hành.

### Đồng bộ khung viền Thẻ Lọc & Bảng dữ liệu (Filter & DataTable Card Container)
Khung chứa bảng dữ liệu (`.data-table-card`, `*Table.vue`) và toàn bộ form tìm kiếm / bộ lọc (`shared/ui/FilterBar/`: `FilterBar`, `AdvancedFilterBar`, `FilterCard`, `FilterToolbar`, và `ScheduleListView`) tuân thủ nghiêm ngặt bộ 4 token tạo khung chuẩn Datacake:
- `background-color: var(--surface-card);`
- `border: 1px solid var(--border-default);`
- `border-radius: 0.5rem;`
- `box-shadow: var(--shadow-control);`

Tuyệt đối không kết hợp `border: 1px solid var(--border-default)` với `box-shadow: var(--shadow-card)` (vì token `--shadow-card` có chứa ring `0 0 0 1px` tạo viền kép sẫm màu dày gấp đôi gây cảm giác nặng nề).

`<style scoped>` trong SFC; vượt ~50 dòng thì ra file `.css` cạnh component và dùng `<style scoped src="./X.css">` — `src` và `scoped` dùng được cùng nhau.

Mọi animation tôn trọng `prefers-reduced-motion`. Riêng `Spinner` thì **làm chậm chứ không dừng**: spinner đứng im không còn là chỉ báo tải.

`:focus-visible` không bao giờ bị xoá.

### `PageSizeSelect` (Component chọn kích thước trang chuẩn Datacake Custom Dropdown)

Nằm trong `shared/ui/DataTable/PageSizeSelect.vue`, phục vụ phân trang `DataTablePagination`:
- **Giao diện Custom Dropdown**: Button trigger với SVG chevron `M6 8l4 4 4-4` xoay 180° khi mở menu, viền focus xanh Datacake `#2563eb` outline-offset -1px kép không khe hở.
- **Menu Popup Floating**: Danh sách lựa chọn nổi bên dưới trigger, mục đang chọn phủ màu xanh dương Datacake `#2563eb` chữ trắng tinh (`media_1788672928559.png`), mục hover có nền xám dịu `#f1f5f9` (dark `#334155`).
- **Tương tác & Trợ năng**: Hỗ trợ đầy đủ phím Enter / Space / Escape / ArrowDown / ArrowUp, click-outside tự động đóng, đồng bộ `<select>` ẩn `.sr-only` cho Screen Reader và biểu mẫu.
- **Chuẩn hóa giá trị**: Tự động chèn `modelValue` vào danh sách nếu thiếu, sắp xếp tăng dần, khử trùng lặp.

### `ActionToolbar` & `PageHeader` (Chế độ Đồng Hàng Datacake Single-Line Mode)

Phục vụ thanh điều khiển và tiêu đề bảng dữ liệu chuẩn Datacake (`plans/260910-2115-datacake-action-header-inline/`):
- **Khóa cứng một hàng ngang (`flex-wrap: nowrap`)**: Tiêu đề trang (`<h1>`) và bộ 3 điều khiển (`SearchInput` + `ColumnPicker` + `Button`) luôn nằm trên cùng một trục hoành, triệt tiêu hoàn toàn lỗi gãy 2 hàng.
- **Co gọn lũy tiến qua CSS Container Queries (`@container`)**:
  - Khi container rộng (> 840px): Hiển thị đầy đủ tiêu đề, mô tả phụ, ô search 240px, nhãn "Cột ˇ" và nút "+ Thêm mới".
  - Khi container hẹp (540px – 840px): `Button` tự động co thành icon 38×38px vuông vắn; `ColumnPicker` ẩn nhãn chữ chỉ hiển thị icon bảng; `SearchInput` co giãn linh hoạt 140px – 180px; tiêu đề tự động ellipsis.
  - Khi container cực hẹp (< 540px): Ẩn mô tả phụ, tiêu đề cắt ba chấm, toàn bộ 3 control giữ nguyên dạng icon 38px không tràn viền.
- **Trợ năng (A11y WCAG 2.2)**: Mọi nút co thành icon bắt buộc duy trì `aria-label` và `title` native cho tooltip và trình đọc màn hình.

### `Breadcrumb` (Đường Dẫn Phân Cấp Chuẩn Datacake Live Parity)

Nằm trong `shared/ui/Breadcrumb.vue`, chuẩn hóa 1:1 theo dữ liệu Chrome DevTools Protocol trích xuất trực tiếp từ Datacake production (`app.datacake.de`):
- **Hình thái mũi tên (Chevron Separator)**: Heroicons Solid 20×20 (`viewBox="0 0 20 20"`, fill `currentColor`, stroke `none`), kích thước 20×20px (`inline-size: 1.25rem`), màu `text-gray-400` (`#9ca3af`), căn giữa tuyệt đối đồng trục với dòng chữ.
- **Độ dày chữ (Font Weight Parity)**: Đồng nhất **`font-weight: 500` (Medium)** trên toàn bộ chuỗi phân cấp (mục cha, text tĩnh, trang hiện tại). Triệt tiêu hoàn toàn sự khập khiễng giữa nét mảnh 400 và nét đậm 600.
- **Kích thước & Dòng (Typography Scale)**: Cỡ chữ chuẩn `text-sm` = **14px** (`0.875rem`), chiều cao dòng **20px** (`1.25rem`).
- **Hệ màu Neutral Gray 3 nấc**:
  - Separator: `#9ca3af` (`text-gray-400`).
  - Link cha & Static: `#6b7280` (`text-gray-500`).
  - Hover & Current: `#374151` (`text-gray-700`), **tuyệt đối không gạch chân (no underline)** khi hover, chuyển màu êm ái với `transition: color 150ms ease-in-out`.
- **Trợ năng & Bất biến Semantic**:
  - Khung `<nav class="crumbs" aria-label="Đường dẫn">` với `<ol>` và `<li>`.
  - Mục cuối là `<span aria-current="page">` không thể click và không nhận focus, tránh bẫy điều hướng rỗng.
  - SVG separator trang bị `aria-hidden="true"` để không đọc thừa ký tự phân cách trên Screen Reader.


