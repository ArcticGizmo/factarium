import { createRouter, createWebHistory } from 'vue-router';
import type { RouteRecordRaw } from 'vue-router';
import OverviewPage from '../features/overview/OverviewPage.vue';
import DataHealthPage from '../features/data-health/DataHealthPage.vue';
import RepositoriesPage from '../features/repositories/RepositoriesPage.vue';
import DeliveryPage from '../features/delivery/DeliveryPage.vue';
import FlowPage from '../features/flow/FlowPage.vue';
import ClaudeCodePage from '../features/claude-code/ClaudeCodePage.vue';
import PeoplePage from '../features/people/PeoplePage.vue';
import GitHubIntegrationsPage from '../features/integrations/GitHubIntegrationsPage.vue';
import JiraIntegrationsPage from '../features/integrations/JiraIntegrationsPage.vue';
import TempoIntegrationsPage from '../features/integrations/TempoIntegrationsPage.vue';
import ClaudeIntegrationsPage from '../features/integrations/ClaudeIntegrationsPage.vue';
import PipelinePage from '../features/integrations/PipelinePage.vue';
import SyncActivityPage from '../features/integrations/SyncActivityPage.vue';
import IntegrationRecordsPage from '../features/integrations/IntegrationRecordsPage.vue';

export const routes: RouteRecordRaw[] = [
  { path: '/', redirect: '/overview' },
  { path: '/overview', name: 'overview', component: OverviewPage },
  { path: '/data-health', name: 'data-health', component: DataHealthPage },
  { path: '/repositories', name: 'repositories', component: RepositoriesPage },
  { path: '/delivery', name: 'delivery', component: DeliveryPage },
  { path: '/flow', name: 'flow', component: FlowPage },
  { path: '/claude-code', name: 'claude-code', component: ClaudeCodePage },
  { path: '/people', name: 'people', component: PeoplePage },
  { path: '/integrations', redirect: '/integrations/github' },
  { path: '/integrations/github', name: 'integrations-github', component: GitHubIntegrationsPage },
  { path: '/integrations/jira', name: 'integrations-jira', component: JiraIntegrationsPage },
  { path: '/integrations/tempo', name: 'integrations-tempo', component: TempoIntegrationsPage },
  { path: '/integrations/claude', name: 'integrations-claude', component: ClaudeIntegrationsPage },
  { path: '/integrations/activity', name: 'integrations-activity', component: SyncActivityPage },
  { path: '/integrations/pipeline', name: 'integrations-pipeline', component: PipelinePage },
  {
    path: '/integrations/:id/records',
    name: 'integration-records',
    component: IntegrationRecordsPage
  },
  // Catch-all: any unknown path falls back to the overview.
  { path: '/:pathMatch(.*)*', name: 'not-found', redirect: '/overview' }
];

export default createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes
});
