import { createRouter, createWebHistory } from 'vue-router'
import { routes } from './routes'

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior() {
    return { top: 0 }
  },
})

router.beforeEach((to, _from, next) => {
  // Update document title
  const title = to.meta.title ? `${to.meta.title} — HMS-SERVER` : 'HMS-SERVER — Industrial SCADA'
  if (typeof document !== 'undefined') {
    document.title = title
  }

  // Allow navigation (Session validation is reactive via API 401 interceptor)
  next()
})
