import { createRouter, createWebHistory } from 'vue-router';
import type { RouteRecordRaw } from 'vue-router';
import LiveSummaryPanel from '../components/LiveSummaryPanel.vue';
import RepoActivityDashboard from '../components/RepoActivityDashboard.vue';
import DeliveryDashboard from '../components/DeliveryDashboard.vue';
import ClaudeCodeDashboard from '../components/ClaudeCodeDashboard.vue';
import IdentityMappingPanel from '../components/IdentityMappingPanel.vue';
import SchedulesPanel from '../components/SchedulesPanel.vue';

export const routes: RouteRecordRaw[] = [
  { path: '/', redirect: '/overview' },
  { path: '/overview', name: 'overview', component: LiveSummaryPanel },
  {
    path: '/repositories',
    name: 'repositories',
    component: RepoActivityDashboard
  },
  { path: '/delivery', name: 'delivery', component: DeliveryDashboard },
  { path: '/claude-code', name: 'claude-code', component: ClaudeCodeDashboard },
  { path: '/people', name: 'people', component: IdentityMappingPanel },
  { path: '/sources', name: 'sources', component: SchedulesPanel }
];

export default createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes
});
