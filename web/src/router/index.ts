import { createRouter, createWebHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'
import LiveSummaryPanel from '../components/LiveSummaryPanel.vue'
import RepoActivityDashboard from '../components/RepoActivityDashboard.vue'
import DeliveryDashboard from '../components/DeliveryDashboard.vue'
import ClaudeCodeDashboard from '../components/ClaudeCodeDashboard.vue'
import IdentityMappingPanel from '../components/IdentityMappingPanel.vue'
import SchedulesPanel from '../components/SchedulesPanel.vue'

// This table maps URLs to page components only. A page's title/identity is
// supplied by the page itself via the <BasePage> layout, and the left-nav menu
// is authored in App.vue via <NavItem> — both independent of this table.
export const routes: RouteRecordRaw[] = [
  { path: '/', redirect: '/overview' },
  { path: '/overview', name: 'overview', component: LiveSummaryPanel },
  { path: '/repositories', name: 'repositories', component: RepoActivityDashboard },
  { path: '/delivery', name: 'delivery', component: DeliveryDashboard },
  { path: '/claude-code', name: 'claude-code', component: ClaudeCodeDashboard },
  { path: '/people', name: 'people', component: IdentityMappingPanel },
  { path: '/sources', name: 'sources', component: SchedulesPanel },
]

export default createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
})
