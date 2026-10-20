<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'

/**
 * Calendar popover chuẩn Datacake — dựng lại theo DOM thật trích từ production
 * (specs/012-datacake-history-sync/datacake-history-dom-reference.md):
 * - Monday-first, KHÔNG render ngày tháng kề (chỉ 1 ô blank leading).
 * - Click ngày áp dụng NGAY (không có nút Discard/Apply): click đầu chọn
 *   single-day, click thứ hai mở range; textbox ngoài cập nhật tức thời.
 * - Hover khi đang có start: preview range (nền xanh nhạt dc-cell--preview, đầu hover bo tròn).
 * - From/Until: 3 <select> native mỗi bên (giờ 12h, phút bước 5, AM/PM).
 */

const props = defineProps<{
  from: string
  to: string
}>()

const emit = defineEmits<{
  apply: [from: string, to: string]
  close: []
}>()

const initialFromDate = new Date(props.from)
const initialToDate = new Date(props.to)
const validFrom = !Number.isNaN(initialFromDate.getTime())
const validTo = !Number.isNaN(initialToDate.getTime())

const viewYear = ref(validFrom ? initialFromDate.getFullYear() : new Date().getFullYear())
const viewMonth = ref(validFrom ? initialFromDate.getMonth() : new Date().getMonth())

const dayOnly = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate())
const selectedStartDate = ref<Date | null>(validFrom ? dayOnly(initialFromDate) : null)
const selectedEndDate = ref<Date | null>(validTo ? dayOnly(initialToDate) : null)
const hoverDate = ref<Date | null>(null)

// ── Time selects (12h, phút bước 5, AM/PM — đúng option set của Datacake) ──
const HOUR_OPTIONS = [12, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11] as const
const MINUTE_OPTIONS = [0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55] as const

const hour12Of = (d: number) => d % 12 // 0→12 hiển thị, value select là 0..11

const fromHour = ref<number>(validFrom ? hour12Of(initialFromDate.getHours()) : 0)
const fromMinute = ref<number>(
  validFrom ? Math.round(initialFromDate.getMinutes() / 5) * 5 % 60 : 0,
)
const fromMeridiem = ref<'am' | 'pm'>(validFrom && initialFromDate.getHours() >= 12 ? 'pm' : 'am')

const toHour = ref<number>(validTo ? hour12Of(initialToDate.getHours()) : 0)
const toMinute = ref<number>(validTo ? Math.round(initialToDate.getMinutes() / 5) * 5 % 60 : 0)
const toMeridiem = ref<'am' | 'pm'>(validTo && initialToDate.getHours() >= 12 ? 'pm' : 'am')

const pad = (n: number) => n.toString().padStart(2, '0')
const hourLabel = (h: number) => (h === 0 ? '12' : pad(h))

function toHour24(h12: number, meridiem: 'am' | 'pm'): number {
  return (h12 % 12) + (meridiem === 'pm' ? 12 : 0)
}

// ── Tháng & lưới ──
const MONTH_NAMES = [
  'Tháng 1', 'Tháng 2', 'Tháng 3', 'Tháng 4', 'Tháng 5', 'Tháng 6',
  'Tháng 7', 'Tháng 8', 'Tháng 9', 'Tháng 10', 'Tháng 11', 'Tháng 12',
]
const WEEKDAYS = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN'] as const

const currentMonthName = computed(() => MONTH_NAMES[viewMonth.value]!)
const currentYearLabel = computed(() => String(viewYear.value))

function prevMonth(): void {
  if (viewMonth.value === 0) {
    viewMonth.value = 11
    viewYear.value -= 1
  } else {
    viewMonth.value -= 1
  }
}

function nextMonth(): void {
  if (viewMonth.value === 11) {
    viewMonth.value = 0
    viewYear.value += 1
  } else {
    viewMonth.value += 1
  }
}

/** Số ô blank trước ngày 1 — Monday-first: (getDay()+6)%7 với getDay() 0=CN. */
const leadingBlanks = computed(() => (new Date(viewYear.value, viewMonth.value, 1).getDay() + 6) % 7)

interface CalendarDay {
  day: number
  date: Date
  wrapperCls: string
  btnCls: string
}

/** Hôm nay theo giờ máy — Datacake in đậm ngày hiện tại của tháng đang xem. */
function isTodayOfDay(day: number): boolean {
  const t = new Date()
  return t.getFullYear() === viewYear.value && t.getMonth() === viewMonth.value && t.getDate() === day
}

/**
 * Class từng ngày theo trạng thái đo được từ Datacake thật:
 * - start/end: wrapper dc-cell--sel + bo tròn đầu; nút chữ trắng (end thêm font-bold).
 * - đang preview (có start, chưa có end, đang hover): giữa = nền preview nhạt,
 *   ngày hover mang rounded cuối range theo hướng.
 * - single-day: pill bo tròn cả hai đầu.
 */
const calendarDays = computed<CalendarDay[]>(() => {
  const daysInMonth = new Date(viewYear.value, viewMonth.value + 1, 0).getDate()
  const s = selectedStartDate.value?.getTime() ?? null
  const e = selectedEndDate.value?.getTime() ?? null
  const h = hoverDate.value?.getTime() ?? null

  const previewing = s !== null && e === null && h !== null && h !== s
  const lo = previewing ? Math.min(s!, h!) : s !== null && e !== null ? Math.min(s, e) : null
  const hi = previewing ? Math.max(s!, h!) : s !== null && e !== null ? Math.max(s, e) : null

  const days: CalendarDay[] = []
  for (let d = 1; d <= daysInMonth; d++) {
    const date = new Date(viewYear.value, viewMonth.value, d)
    const t = date.getTime()

    // 'dc-cell' mang transition nền 80ms — xem cuối <style>.
    let wrapperCls = 'px-1 mb-1 dc-cell'
    let btnCls = 'dc-day'

    // Đang preview: mọi ngày TRƯỚC start bị mờ và mất hover — đo được từ
    // Datacake thật (start=12, hover=18: ngày 1-11 mờ màu gray-400).
    const dimmed = previewing && s !== null && t < s

    if (dimmed) {
      btnCls = 'dc-day dc-day--dim'
    } else if (s !== null && t === s) {
      if (e !== null && e !== s) {
        wrapperCls += ' dc-cell--sel ' + (s < e ? 'dc-cell--l' : 'dc-cell--r')
        btnCls = 'dc-day dc-day--sel'
      } else if (previewing) {
        wrapperCls += ' dc-cell--sel ' + (h! > s ? 'dc-cell--l' : 'dc-cell--r')
        btnCls = 'dc-day dc-day--sel'
      } else {
        wrapperCls += ' dc-cell--sel dc-cell--l dc-cell--r'
        btnCls = 'dc-day dc-day--sel'
      }
    } else if (e !== null && t === e && e !== s) {
      wrapperCls += ' dc-cell--sel ' + (e > s! ? 'dc-cell--r' : 'dc-cell--l')
      btnCls = 'dc-day dc-day--sel dc-day--end'
    } else if ((lo !== null && hi !== null && t > lo && t < hi) || (previewing && t === hi)) {
      wrapperCls += ' dc-cell--preview'
      if (previewing && t === hi) {
        wrapperCls += h! > s! ? ' dc-cell--r' : ' dc-cell--l'
      }
    }

    days.push({ day: d, date, wrapperCls, btnCls: isTodayOfDay(d) ? `${btnCls} dc-day--today` : btnCls })
  }
  return days
})

function onSelectDay(day: CalendarDay): void {
  if (!selectedStartDate.value || (selectedStartDate.value && selectedEndDate.value)) {
    // Click đầu tiên: from = to = ngày đó (áp dụng NGAY), nhưng end nội bộ
    // giữ null để preview hover hoạt động — đúng hành vi Datacake đo được.
    selectedStartDate.value = day.date
    selectedEndDate.value = null
  } else if (day.date.getTime() < selectedStartDate.value.getTime()) {
    selectedEndDate.value = selectedStartDate.value
    selectedStartDate.value = day.date
  } else {
    selectedEndDate.value = day.date
  }
  // Con trỏ thường VẪN ĐANG đè lên ngày vừa click — hover bám theo để
  // preview không nhấp nháy tắt rồi bật lại khi người dùng bắt đầu rê.
  hoverDate.value = day.date
  emitApply()
}

/**
 * Theo dõi hover bằng MỘT listener `pointermove` trên cả lưới thay vì
 * `mouseover` từng nút: pointermove phát liên tục theo vệt chuột nên không
 * bỏ ô khi rê nhanh qua khe `px-1` giữa các cell (nguồn "chập chờn" cũ).
 * Chuột trên khe/ô trống → giữ preview cũ (đúng Datacake).
 *
 * Gán hoverDate ĐỒNG BỘ, KHÔNG qua requestAnimationFrame: rAF bị throttle
 * tới mức bỏ đói khi webview không composite (tab nền/occluded) — đo thực:
 * pointermove vẫn tới nhưng preview không bao giờ vẽ. Computed 30 phần tử
 * là đủ rẻ để chạy thẳng mỗi event.
 */
function onGridPointerMove(e: Event): void {
  const target = (e.target as HTMLElement | null)?.closest?.('[data-day]')
  if (!target) return
  const n = Number(target.getAttribute('data-day'))
  if (!Number.isInteger(n) || n < 1) return
  hoverDate.value = new Date(viewYear.value, viewMonth.value, n)
}

function onLeaveGrid(): void {
  hoverDate.value = null
}

function emitApply(): void {
  const start = selectedStartDate.value
  const end = selectedEndDate.value ?? selectedStartDate.value
  if (!start || !end) return

  const f = new Date(
    start.getFullYear(), start.getMonth(), start.getDate(),
    toHour24(fromHour.value, fromMeridiem.value), fromMinute.value, 0,
  )
  const t = new Date(
    end.getFullYear(), end.getMonth(), end.getDate(),
    toHour24(toHour.value, toMeridiem.value), toMinute.value, 0,
  )
  emit('apply', f.toISOString(), t.toISOString())
}

function onTimeChange(): void {
  emitApply()
}

function onKeydown(e: KeyboardEvent): void {
  if (e.key === 'Escape') {
    emit('close')
  }
}

onMounted(() => {
  if (typeof window !== 'undefined') {
    window.addEventListener('keydown', onKeydown)
  }
})

onUnmounted(() => {
  if (typeof window !== 'undefined') {
    window.removeEventListener('keydown', onKeydown)
  }
})

// KHÔNG watch props.from/to: popover là nguồn sự thật khi đang mở. Watcher cũ
// ghi đè selectedEndDate ngay khi apply → toolbar → view đổi range → props đổi
// (vòng ~250ms debounce) → end ≠ null → preview hover chết sau mỗi lần click
// — chính là "chập chờn lúc được lúc không" đo được trên trang thật. Calendar
// unmount khi đóng popover nên lần mở sau vẫn nhận đúng range từ props lúc init.
</script>

<template>
  <div
    class="bg-white z-30 rounded-lg shadow-sm p-4 w-64"
    role="dialog"
    aria-modal="true"
    aria-label="Bộ chọn khoảng ngày và giờ Datacake"
  >
    <div>
      <!-- Header tháng năm và nút prev/next -->
      <div class="flex justify-between items-center mb-2">
        <div>
          <span class="text-lg font-bold dc-cal-strong">{{ currentMonthName }}</span>
          <span class="ml-1 text-lg font-normal dc-cal-muted">{{ currentYearLabel }}</span>
        </div>
        <div>
          <button
            type="button"
            class="transition ease-in-out duration-100 inline-flex cursor-pointer p-1 rounded-full dc-nav-btn"
            aria-label="Tháng trước"
            @click="prevMonth"
          >
            <svg class="h-6 w-6 inline-flex dc-cal-icon" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
            </svg>
          </button>
          <button
            type="button"
            class="transition ease-in-out duration-100 inline-flex cursor-pointer p-1 rounded-full dc-nav-btn"
            aria-label="Tháng sau"
            @click="nextMonth"
          >
            <svg class="h-6 w-6 inline-flex dc-cal-icon" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7" />
            </svg>
          </button>
        </div>
      </div>

      <!-- Hàng tiêu đề tuần: Monday-first chuẩn Datacake -->
      <div class="flex flex-wrap mb-3 -mx-1">
        <div v-for="wd in WEEKDAYS" :key="wd" class="px-1" style="width: 14.26%">
          <div class="font-medium text-center text-xs dc-weekday">{{ wd }}</div>
        </div>
      </div>

      <!-- Lưới ngày: chỉ ô blank leading, không render ngày tháng kề.
           Hover theo dõi ở LƯỚI (pointermove) — chi tiết trong script. -->
      <div class="flex flex-wrap -mx-1" @pointermove="onGridPointerMove" @pointerleave="onLeaveGrid" @mouseleave="onLeaveGrid">
        <div
          v-for="n in leadingBlanks"
          :key="'blank-' + n"
          class="text-center border p-1 border-transparent text-sm"
          style="width: 14.28%"
        ></div>
        <div
          v-for="item in calendarDays"
          :key="item.day"
          :class="item.wrapperCls"
          style="width: 14.28%"
        >
          <button
            type="button"
            class="inline-flex w-full justify-center cursor-pointer text-center text-sm rounded-full leading-loose transition ease-in-out duration-100"
            :class="item.btnCls"
            :data-day="item.day"
            @click="onSelectDay(item)"
          >
            {{ item.day }}
          </button>
        </div>
      </div>

      <!-- From/Until: 3 select native mỗi bên (giờ 12h · phút bước 5 · AM/PM) -->
      <div class="grid grid-cols-2 mt-3">
        <div>
          <label class="block text-sm font-medium dc-time-label" for="datacake-cal-from-hour">Từ</label>
          <div class="bg-white rounded-md shadow-xs">
            <div class="flex items-center">
              <select
                id="datacake-cal-from-hour"
                v-model.number="fromHour"
                class="text-sm appearance-none outline-hidden border-0 bg-none px-2"
                aria-label="Giờ bắt đầu"
                @change="onTimeChange"
              >
                <option v-for="h in HOUR_OPTIONS" :key="h" :value="h % 12">{{ hourLabel(h % 12) }}</option>
              </select>
              <span class="text-sm">:</span>
              <select
                v-model.number="fromMinute"
                class="text-sm appearance-none outline-hidden border-0 bg-none px-2"
                aria-label="Phút bắt đầu"
                @change="onTimeChange"
              >
                <option v-for="m in MINUTE_OPTIONS" :key="m" :value="m">{{ pad(m) }}</option>
              </select>
              <select
                v-model="fromMeridiem"
                class="text-sm appearance-none outline-hidden border-0 bg-none px-2"
                aria-label="Sáng/chiều bắt đầu"
                @change="onTimeChange"
              >
                <option value="am">AM</option>
                <option value="pm">PM</option>
              </select>
            </div>
          </div>
        </div>
        <div>
          <label class="block text-sm font-medium dc-time-label" for="datacake-cal-until-hour">Đến</label>
          <div class="bg-white rounded-md shadow-xs">
            <div class="flex items-center">
              <select
                id="datacake-cal-until-hour"
                v-model.number="toHour"
                class="text-sm appearance-none outline-hidden border-0 bg-none px-2"
                aria-label="Giờ kết thúc"
                @change="onTimeChange"
              >
                <option v-for="h in HOUR_OPTIONS" :key="h" :value="h % 12">{{ hourLabel(h % 12) }}</option>
              </select>
              <span class="text-sm">:</span>
              <select
                v-model.number="toMinute"
                class="text-sm appearance-none outline-hidden border-0 bg-none px-2"
                aria-label="Phút kết thúc"
                @change="onTimeChange"
              >
                <option v-for="m in MINUTE_OPTIONS" :key="m" :value="m">{{ pad(m) }}</option>
              </select>
              <select
                v-model="toMeridiem"
                class="text-sm appearance-none outline-hidden border-0 bg-none px-2"
                aria-label="Sáng/chiều kết thúc"
                @change="onTimeChange"
              >
                <option value="am">AM</option>
                <option value="pm">PM</option>
              </select>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* Màu qua var(..., hex) — hex là giá trị đo được từ Datacake production
   (specs/012 datacake-history-dom-reference). Không dùng utility màu Tailwind
   trong template để cổng tương phản (GATE 9) đo được. */

.dc-cal-strong {
  color: var(--text-primary, #1f2937);
}

.dc-cal-muted {
  color: var(--text-secondary, #4b5563);
}

.dc-nav-btn:hover {
  background-color: var(--surface-hover, #e5e7eb);
}

.dc-cal-icon {
  color: var(--text-muted, #6b7280);
}

.dc-weekday {
  color: var(--text-primary, #1f2937);
}

.dc-time-label {
  color: var(--text-primary, #374151);
}

/* Trạng thái ngày — pill highlight nằm ở WRAPPER (đúng DOM Datacake) */
.dc-day {
  color: var(--text-primary, #374151);
}

.dc-day:hover {
  background-color: #bfdbfe;
}

.dc-day--sel,
.dc-day--sel:hover {
  color: #ffffff;
  background: none;
}

.dc-day--end {
  font-weight: 700;
}

/* Ngày trước start khi đang preview — mờ, không hover (đúng Datacake) */
.dc-day--dim {
  color: var(--text-muted, #9ca3af);
}

.dc-day--dim:hover {
  background: none;
}

/* Hôm nay — Datacake in đậm ngày hiện tại (đo được: ngày 5 font-bold) */
.dc-day--today {
  font-weight: 700;
}

/* Nền cell chuyển 80ms — mượt hơn flip tức thì, không lag bằng transition nút (100ms) */
.dc-cell {
  transition: background-color 80ms ease-out;
}

.dc-cell--sel {
  background-color: var(--status-info, #3b82f6);
}

.dc-cell--preview {
  background-color: #bfdbfe;
}

.dc-cell--l {
  border-top-left-radius: 9999px;
  border-bottom-left-radius: 9999px;
}

.dc-cell--r {
  border-top-right-radius: 9999px;
  border-bottom-right-radius: 9999px;
}
</style>
