import { computed, ref, type ComputedRef, type Ref } from 'vue'

export interface StepItem<T = string> {
  readonly id: T
  readonly label: string
  readonly icon?: string
  readonly disabled?: boolean
}

export interface UseStepNavigationOptions<T = string> {
  readonly steps: readonly StepItem<T>[]
  readonly initialStepId?: T
  readonly onStepChange?: (step: StepItem<T>, index: number) => void
}

export interface UseStepNavigationReturn<T = string> {
  readonly steps: readonly StepItem<T>[]
  readonly currentIndex: Ref<number>
  readonly currentStep: ComputedRef<StepItem<T> | undefined>
  readonly prevStep: ComputedRef<StepItem<T> | null>
  readonly nextStep: ComputedRef<StepItem<T> | null>
  readonly canGoPrev: ComputedRef<boolean>
  readonly canGoNext: ComputedRef<boolean>
  readonly totalSteps: ComputedRef<number>
  readonly progressPercent: ComputedRef<number>
  goPrev: () => boolean
  goNext: () => boolean
  goToId: (id: T) => boolean
  goToIndex: (index: number) => boolean
  reset: () => void
}

/**
 * Composable quản lý điều hướng tuần tự qua các bước / tabs / wizard.
 * Tuân thủ FSD shared/ui — phi nghiệp vụ, zero dependency ngoài Vue.
 */
export function useStepNavigation<T = string>(
  options: UseStepNavigationOptions<T>,
): UseStepNavigationReturn<T> {
  const { steps, initialStepId, onStepChange } = options

  const resolveInitialIndex = (): number => {
    if (steps.length === 0) return 0
    if (initialStepId !== undefined) {
      const found = steps.findIndex((s) => s.id === initialStepId)
      if (found !== -1) return found
    }
    return 0
  }

  const currentIndex = ref<number>(resolveInitialIndex())

  const totalSteps = computed(() => steps.length)

  const currentStep = computed(() => {
    if (steps.length === 0) return undefined
    return steps[currentIndex.value]
  })

  const canGoPrev = computed(() => {
    return steps.length > 1 && currentIndex.value > 0
  })

  const canGoNext = computed(() => {
    return steps.length > 1 && currentIndex.value < steps.length - 1
  })

  const prevStep = computed(() => {
    if (!canGoPrev.value) return null
    return steps[currentIndex.value - 1] ?? null
  })

  const nextStep = computed(() => {
    if (!canGoNext.value) return null
    return steps[currentIndex.value + 1] ?? null
  })

  const progressPercent = computed(() => {
    if (steps.length === 0) return 0
    return Math.round(((currentIndex.value + 1) / steps.length) * 100)
  })

  function notifyChange(index: number): void {
    const step = steps[index]
    if (step && onStepChange) {
      onStepChange(step, index)
    }
  }

  function goPrev(): boolean {
    if (!canGoPrev.value) return false
    currentIndex.value -= 1
    notifyChange(currentIndex.value)
    return true
  }

  function goNext(): boolean {
    if (!canGoNext.value) return false
    currentIndex.value += 1
    notifyChange(currentIndex.value)
    return true
  }

  function goToIndex(index: number): boolean {
    if (index < 0 || index >= steps.length || index === currentIndex.value) {
      return false
    }
    currentIndex.value = index
    notifyChange(currentIndex.value)
    return true
  }

  function goToId(id: T): boolean {
    const found = steps.findIndex((s) => s.id === id)
    if (found === -1 || found === currentIndex.value) {
      return false
    }
    currentIndex.value = found
    notifyChange(currentIndex.value)
    return true
  }

  function reset(): void {
    const initIdx = resolveInitialIndex()
    if (currentIndex.value !== initIdx) {
      currentIndex.value = initIdx
      notifyChange(currentIndex.value)
    }
  }

  return {
    steps,
    currentIndex,
    currentStep,
    prevStep,
    nextStep,
    canGoPrev,
    canGoNext,
    totalSteps,
    progressPercent,
    goPrev,
    goNext,
    goToId,
    goToIndex,
    reset,
  }
}
