/** "09:13:20 (2 phút trước)": the time a value was read from a device and how old it is now. */
export function formatReadTime(readAt: string | number | Date, now: number): string {
  const read = new Date(readAt)
  const seconds = Math.max(0, Math.round((now - read.getTime()) / 1000))
  const age = seconds < 60 ? 'vừa xong' : seconds < 3600
    ? `${Math.floor(seconds / 60)} phút trước` : `${Math.floor(seconds / 3600)} giờ trước`
  return `${read.toLocaleTimeString()} (${age})`
}
