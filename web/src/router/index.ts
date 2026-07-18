import { createRouter, createWebHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'
import LiveSummaryPanel from '../components/LiveSummaryPanel.vue'
import RepoActivityDashboard from '../components/RepoActivityDashboard.vue'
import DeliveryDashboard from '../components/DeliveryDashboard.vue'
import ClaudeCodeDashboard from '../components/ClaudeCodeDashboard.vue'
import IdentityMappingPanel from '../components/IdentityMappingPanel.vue'
import SchedulesPanel from '../components/SchedulesPanel.vue'

// `title` is the page's own heading (shown in the app bar) — a route concern.
// The left-nav menu (labels, icons, order, visibility) is authored separately
// in App.vue via <NavItem>, so nav display is decoupled from this table.
declare module 'vue-router' {
  interface RouteMeta {
    title?: string
  }
}

export const routes: RouteRecordRaw[] = [
  { path: '/', redirect: '/overview' },
  {
    path: '/overview',
    name: 'overview',
    component: LiveSummaryPanel,
    meta: { title: 'Overview' },
  },
  {
    path: '/repositories',
    name: 'repositories',
    component: RepoActivityDashboard,
    meta: { title: 'Repositories' },
  },
  {
    path: '/delivery',
    name: 'delivery',
    component: DeliveryDashboard,
    meta: { title: 'Delivery' },
  },
  {
    path: '/claude-code',
    name: 'claude-code',
    component: ClaudeCodeDashboard,
    meta: { title: 'Claude Code' },
  },
  {
    path: '/people',
    name: 'people',
    component: IdentityMappingPanel,
    meta: { title: 'People' },
  },
  {
    path: '/sources',
    name: 'sources',
    component: SchedulesPanel,
    meta: { title: 'Sources' },
  },
]

export default createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
})
