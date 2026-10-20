/**
 * Hợp đồng kiểu dữ liệu chuẩn cho khung trang danh sách (StandardListPage).
 * Áp dụng thống nhất cho toàn bộ 18 màn hình danh sách trong hệ thống.
 */

export interface StandardListPageProps {
  /** Tiêu đề chính của màn hình */
  title: string
  /** Phụ đề giải thích hoặc hướng dẫn nghiệp vụ ngắn gọn */
  description?: string
  /** Chế độ hiển thị tiêu đề và toolbar trên cùng một dòng (Datacake single line) */
  singleLine?: boolean
  /** Kích hoạt bố cục chia đôi (Hybrid Split Layout) cho các trang có panel phụ trợ (như Sensor Tree) */
  splitLayout?: boolean
  /** Độ rộng khung trang bên trong AppShell: 'full' (100% cho bảng dữ liệu), 'default', 'form' */
  width?: 'full' | 'default' | 'prose' | 'form'
  /** Bật/tắt thanh Breadcrumb tự động */
  breadcrumb?: boolean
  /** Bật/tắt thanh kéo phân cách (Resizable Splitter Handle) cho cột sidebar */
  resizableSplit?: boolean
  /** Độ rộng mặc định sidebar (px, mặc định 300) */
  sidebarWidth?: number
  /** Giới hạn nhỏ nhất cho sidebar (px, mặc định 240) */
  minSidebarWidth?: number
  /** Giới hạn lớn nhất cho sidebar (px, mặc định 520) */
  maxSidebarWidth?: number
  /** Khóa định danh lưu vị trí trong localStorage */
  storageKey?: string
}

