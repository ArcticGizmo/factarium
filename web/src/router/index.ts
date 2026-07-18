import { createRouter, createWebHistory } from 'vue-router';
import type { RouteRecordRaw } from 'vue-router';
import OverviewPage from '../features/overview/OverviewPage.vue';
import RepositoriesPage from '../features/repositories/RepositoriesPage.vue';
import DeliveryPage from '../features/delivery/DeliveryPage.vue';
import ClaudeCodePage from '../features/claude-code/ClaudeCodePage.vue';
import PeoplePage from '../features/people/PeoplePage.vue';
import SourcesPage from '../features/sources/SourcesPage.vue';

export const routes: RouteRecordRaw[] = [
  { path: '/', redirect: '/overview' },
  { path: '/overview', name: 'overview', component: OverviewPage },
  { path: '/repositories', name: 'repositories', component: RepositoriesPage },
  { path: '/delivery', name: 'delivery', component: DeliveryPage },
  { path: '/claude-code', name: 'claude-code', component: ClaudeCodePage },
  { path: '/people', name: 'people', component: PeoplePage },
  { path: '/sources', name: 'sources', component: SourcesPage }
];

export default createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes
});
