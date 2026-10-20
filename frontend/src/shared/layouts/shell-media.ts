/**
 * Breakpoint của khung ứng dụng — **điểm neo duy nhất** cho phía JavaScript.
 *
 * ## Vì sao một hằng số cần một file riêng
 *
 * `AppShell` phải biết mình đang ở "chế độ drawer" hay không, vì đó là điều kiện
 * để lấy sidebar ra khỏi tab order (spec 008 FR-1). Điều kiện đó **không** suy
 * ra được từ CSS: `@media` không nói gì cho JavaScript, và jsdom thì không tính
 * layout nên không có cách nào đọc trạng thái thật trong test.
 *
 * Nên giá trị này tồn tại ở **hai chỗ**: ở đây, và trong `@media (max-width: …)`
 * của `AppShell.css`. Sự trùng lặp đó không xoá được — CSS không đọc được biến
 * JavaScript, và một custom property cũng không dùng được trong điều kiện của
 * `@media`.
 *
 * ## Cái làm sự trùng lặp KHÔNG lệch im lặng
 *
 * `AppShell.test.ts` có một ca đọc `AppShell.css`, trích giá trị breakpoint, và
 * so với hằng số này. Lệch thì đỏ. Ca đó đi kèm một negative control tự gieo giá
 * trị sai để chứng minh phép so sánh bắt được.
 *
 * Không có cổng đó thì đường hỏng là: ai đó đổi `48rem` trong CSS mà không mở
 * file này, và ở dải bề rộng lệch nhau, sidebar **hiển thị bình thường nhưng
 * mang `inert`** — menu nhìn thấy được, bấm chuột được, bàn phím không tới được,
 * trình đọc màn hình bỏ qua hoàn toàn. Nặng hơn lỗi mà spec 008 ra đời để sửa.
 *
 * ## Một chủ, không hai — bản này là bản SỬA
 *
 * Bản đầu khai **hai** hằng số độc lập: một chuỗi media query cho `matchMedia`,
 * một chuỗi giá trị cho cổng. Lý do lúc đó là *"cổng không phải parse"*. Nó sai,
 * và vai phản biện bắt được: `AppShell.vue` đọc **query**, cổng so **giá trị**,
 * và không gì buộc hai chuỗi khớp nhau. Kết quả là cổng vừa cho **xanh giả** —
 * sửa CSS kèm sửa giá trị, `matchMedia` vẫn hỏi số cũ — vừa cho **đỏ giả**: sửa
 * CSS kèm sửa query thì cổng báo đỏ cho một thay đổi đúng.
 *
 * Cái sai không phải hai hằng số. Là **cổng neo vào chỗ dễ so thay vì chỗ được
 * dùng**. Query giờ được **sinh ra** từ giá trị, nên chúng không thể lệch, và cổng
 * vẫn không phải parse gì.
 */
export const NARROW_MAX_WIDTH = '48rem'

/**
 * Query mà `AppShell` truyền cho `matchMedia`. **Sinh** từ `NARROW_MAX_WIDTH`,
 * không gõ lại — xem lý do ở trên.
 */
export const NARROW_MEDIA_QUERY = `(max-width: ${NARROW_MAX_WIDTH})`
