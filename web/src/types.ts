// Shapes of the JSON the API returns. These mirror the anonymous response
// objects the .NET endpoints project; keep them in step with the endpoints
// under src/Factarium.Api/Endpoints when payloads change.

/** A single day on a time-series chart. */
export interface DayPoint {
  day: string;
  value: number;
}

/** A labelled magnitude for a categorical (bar) chart. */
export interface LabelValue {
  label: string;
  value: number;
}

// --- /api/live/summary ---
export interface LiveSummary {
  generatedAt: string;
  pullRequests: { open: number; mergedLast7d: number };
  commits: { last7d: number };
  issues: {
    resolvedLast7d: number;
    completionPct: number;
    done: number;
    total: number;
    byStatus: { status: string; count: number }[];
  };
}

// --- /api/dashboards/repo-activity ---
export interface RepoActivityData {
  totals: {
    commits: number;
    prsOpened: number;
    prsMerged: number;
    repositories: number;
    people: number;
    unmappedIdentities: number;
  };
  commitsByDay: DayPoint[];
  prsOpenedByDay: DayPoint[];
  prsMergedByDay: DayPoint[];
  reviewLatencyByDay: DayPoint[];
  commitsByActor: LabelValue[];
}

// --- /api/dashboards/delivery ---
export interface DeliveryData {
  deployProxyBranch: string;
  totals: {
    deploys: number;
    avgLeadTimeHours: number;
    prsMerged: number;
    issuesResolved: number;
    avgIssueCycleHours: number;
    avgReviewLatencyHours: number;
  };
  deploysByDay: DayPoint[];
  leadTimeByDay: DayPoint[];
  prsMergedByDay: DayPoint[];
  issuesResolvedByDay: DayPoint[];
  cycleTimeByDay: DayPoint[];
}

// --- /api/dashboards/claude-code ---
export interface ClaudeCodeData {
  totals: {
    costUsd: number;
    tokens: number;
    linesAdded: number;
    linesRemoved: number;
    sessions: number;
  };
  costByDay: DayPoint[];
  tokensByDay: DayPoint[];
  linesAddedByDay: DayPoint[];
  costByActor: LabelValue[];
}

// --- /api/identities and /api/people ---
export interface Identity {
  id: number;
  login: string;
  source: string;
  personId: number | null;
  personName: string | null;
}

export interface Person {
  id: number;
  displayName: string;
}

// --- /api/integrations and /api/pipeline ---
export interface GitHubConfig {
  org: string | null;
  repos: string[];
}

export interface JiraConfig {
  baseUrl: string | null;
  email: string | null;
  projectKey: string | null;
  // ISO date; the historical floor for the first sync.
  syncSince: string | null;
  // true = scoped token via the Atlassian API gateway; false = classic token against the site.
  scopedToken: boolean;
}

// One row from GET /api/integrations. `config` carries the type-specific fields
// (null for Claude, which is push-based). Id is the backend Guid.
export interface Integration {
  id: string;
  name: string;
  type: string;
  enabled: boolean;
  scheduleCron: string | null;
  lastRunStatus: string;
  lastRunStartedAt: string | null;
  lastRunCompletedAt: string | null;
  lastRunError: string | null;
  lastRunRecordsWritten: number;
  nextRunAt: string | null;
  hasCredential: boolean;
  config: GitHubConfig | JiraConfig | null;
}

// --- /api/integrations/{id}/records ---
export interface RecordTypeCount {
  entityType: string;
  count: number;
}

export interface RepositorySummary {
  full_name: string | null;
  name: string | null;
  owner_login: string | null;
  description: string | null;
  html_url: string | null;
  default_branch: string | null;
  language: string | null;
  visibility: string | null;
  stargazers_count: number | null;
  forks_count: number | null;
  open_issues_count: number | null;
  pushed_at: string | null;
}

export interface RecordsSummary {
  id: string;
  name: string;
  type: string;
  entityTypes: RecordTypeCount[];
  // Present for GitHub integrations; shown in the header instead of a tab.
  repository: RepositorySummary | null;
}

export interface RawRecordView {
  id: number;
  entityType: string;
  sourceId: string;
  sourceUpdatedAt: string | null;
  firstSeenAt: string;
  fetchedAt: string;
  version: number;
  payload: unknown;
}

export interface RecordsPageResult {
  total: number;
  page: number;
  pageSize: number;
  records: RawRecordView[];
}

export interface PipelineStep {
  name: string;
  lastStatus: string;
  lastItemsProcessed: number;
  lastRunAt: string | null;
}

// --- /api/sync-runs ---
// One recorded execution of an integration's sync. `trigger` is "Scheduled" | "Manual";
// `status` is "Running" | "Success" | "Failed".
export interface SyncRun {
  id: number;
  integrationId: string;
  integrationName: string;
  integrationType: string;
  trigger: string;
  status: string;
  startedAt: string;
  completedAt: string | null;
  recordsWritten: number;
  error: string | null;
}

export interface SyncRunsPageResult {
  total: number;
  page: number;
  pageSize: number;
  runs: SyncRun[];
}
