import { createRouter, createWebHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'
import LiveSummaryPanel from '../components/LiveSummaryPanel.vue'
import RepoActivityDashboard from '../components/RepoActivityDashboard.vue'
import DeliveryDashboard from '../components/DeliveryDashboard.vue'
import ClaudeCodeDashboard from '../components/ClaudeCodeDashboard.vue'
import IdentityMappingPanel from '../components/IdentityMappingPanel.vue'
import SchedulesPanel from '../components/SchedulesPanel.vue'

// Nav metadata carried on each route so the left drawer is derived from the
// route table rather than a parallel list.
declare module 'vue-router' {
  interface RouteMeta {
    title?: string
    icon?: string
    nav?: boolean
  }
}

// Routes ARE the nav: `meta.title`/`meta.icon` drive the left drawer, so a new
// page is a single addition here. `nav: true` opts a route into the drawer.
// First-round IA: Overview is the live pulse; the middle three are the
// "what happened" dashboards; People and Sources are the admin pages.
export const routes: RouteRecordRaw[] = [
  { path: '/', redirect: '/overview' },
  {
    path: '/overview',
    name: 'overview',
    component: LiveSummaryPanel,
    meta: { title: 'Overview', icon: 'mdi-pulse', nav: true },
  },
  {
    path: '/repositories',
    name: 'repositories',
    component: RepoActivityDashboard,
    meta: { title: 'Repositories', icon: 'mdi-source-branch', nav: true },
  },
  {
    path: '/delivery',
    name: 'delivery',
    component: DeliveryDashboard,
    meta: { title: 'Delivery', icon: 'mdi-rocket-launch-outline', nav: true },
  },
  {
    path: '/claude-code',
    name: 'claude-code',
    component: ClaudeCodeDashboard,
    meta: { title: 'Claude Code', icon: 'mdi-robot-outline', nav: true },
  },
  {
    path: '/people',
    name: 'people',
    component: IdentityMappingPanel,
    meta: { title: 'People', icon: 'mdi-account-group-outline', nav: true },
  },
  {
    path: '/sources',
    name: 'sources',
    component: SchedulesPanel,
    meta: { title: 'Sources', icon: 'mdi-sync', nav: true },
  },
]

export default createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
})
