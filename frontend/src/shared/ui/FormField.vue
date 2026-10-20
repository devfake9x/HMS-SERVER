<script setup lang="ts">
import { computed, useId } from 'vue'

/**
 * Nhãn, gợi ý, và thông báo lỗi cho một control — nối đúng bằng ARIA.
 *
 * Nó **không** render control. Control đi qua slot và nhận lại ba thứ đã tính
 * sẵn. Lý do: có `input`, `select`, `textarea`, và control tự viết; nhồi nhãn
 * vào từng cái là nhân bản cùng một logic `aria-describedby` nhiều lần, và bản
 * thứ ba sẽ sai.
 *
 * ```vue
 * <FormField label="Mã trạm" :error="errors.code" hint="Không đổi được sau khi tạo">
 *   <template #default="{ id, describedBy, invalid }">
 *     <Input :id="id" v-model="form.code" :aria-describedby="describedBy" :aria-invalid="invalid" />
 *   </template>
 * </FormField>
 * ```
 *
 * ## Vì sao `aria-live` chứ không `role="alert"`
 *
 * Vùng lỗi **luôn có trong DOM**, kể cả khi không có lỗi, và mang
 * `aria-live="polite"`. Nếu chỉ render khi có lỗi và gắn `role="alert"` thì lỗi
 * trả về từ server lúc mở form sẽ bị đọc ngay khi vào trang, trước cả khi người
 * dùng biết mình đang ở form nào. Vùng live có sẵn thì chỉ **thay đổi** mới
 * được thông báo — đúng thời điểm.
 *
 * ## Vì sao dấu `*` là `aria-hidden`
 *
 * `required` trên control đã được screen reader đọc là "bắt buộc". Dấu hoa thị
 * chỉ dành cho mắt; không ẩn nó thì người dùng nghe "ngôi sao" giữa tên field.
 */

const props = withDefaults(
  defineProps<{
    label: string
    /** Chuỗi lỗi, hoặc rỗng/undefined khi không có lỗi. */
    error?: string
    /** Mô tả thêm. Đọc trước lỗi. */
    hint?: string
    /** Chỉ để hiện dấu `*`. Vẫn phải đặt `required` lên control. */
    required?: boolean
  }>(),
  { error: '', hint: '', required: false },
)

const uid = useId()
const controlId = `${uid}-control`
const hintId = `${uid}-hint`
const errorId = `${uid}-error`

const hasError = computed(() => props.error.trim() !== '')

/**
 * Danh sách id cho `aria-describedby`, ngăn bằng khoảng trắng.
 *
 * Thứ tự có ý nghĩa: gợi ý trước, lỗi sau — screen reader đọc theo đúng thứ tự
 * này, và "định dạng cần dùng" nên nghe trước "cái vừa gõ sai".
 *
 * Trả `undefined` khi rỗng, KHÔNG trả chuỗi rỗng: `aria-describedby=""` là một
 * tham chiếu hỏng, một số screen reader xử lý nó khác nhau.
 */
const describedBy = computed<string | undefined>(() => {
  const ids: string[] = []
  if (props.hint.trim() !== '') ids.push(hintId)
  if (hasError.value) ids.push(errorId)
  return ids.length > 0 ? ids.join(' ') : undefined
})
</script>

<template>
  <div class="field">
    <label class="field__label" :for="controlId">
      {{ label }}
      <span v-if="required" class="field__required" aria-hidden="true">*</span>
    </label>

    <slot
      :id="controlId"
      :described-by="describedBy"
      :invalid="hasError ? true : undefined"
      :error-id="errorId"
    />

    <p v-if="hint" :id="hintId" class="field__hint">{{ hint }}</p>

    <!-- Luôn có trong DOM. Xem giải thích ở đầu file. -->
    <p :id="errorId" class="field__error" aria-live="polite">
      <span v-if="hasError">{{ error }}</span>
    </p>
  </div>
</template>

<style scoped>
/* [ĐO source Datacake account]: label (14px font-medium);
 * input wrapper margin 4px; hint (12px) nằm ngay dưới input. */
.field {
  display: flex;
  flex-direction: column;
}

.field__label {
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--text-label, #373f51);
}

.field__required {
  color: var(--status-danger);
}

/* Khoảng cách label → control như wrapper `my-1` của Datacake */
.field__label + :deep(*) {
  margin-top: 0.25rem;
  margin-bottom: 0.25rem;
}

.field__hint {
  margin: 0;
  font-size: 0.75rem;
  line-height: 1.35;
  color: var(--text-hint, #6b7280);
}

/*
 * Vùng lỗi rỗng KHÔNG chiếm chỗ, nhưng vẫn ở trong DOM cho `aria-live`.
 */
.field__error {
  margin: 0;
  font-size: 0.75rem;
  line-height: 1.35;
  color: var(--status-danger);
}

.field__error:not(:has(span)) {
  /* Không có lỗi: không để lại khoảng trắng giữa các field. */
  display: none;
}
</style>
