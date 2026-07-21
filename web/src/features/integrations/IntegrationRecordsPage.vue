<template>
  <BasePage :title="title" subtitle="Synced records (raw, as stored)">
    <template #actions>
      <v-btn size="small" variant="text" prepend-icon="mdi-arrow-left" :to="backTo">Back</v-btn>
      <v-btn size="small" variant="text" @click="reload">Refresh</v-btn>
    </template>

    <div v-if="error" class="text-error mb-2">{{ error }}</div>

    <!-- Repository details: scoped to one repo, so shown here rather than as a tab. -->
    <v-sheet v-if="repo" rounded border class="pa-3 mb-3">
      <div class="d-flex align-center flex-wrap ga-2">
        <a
          v-if="repo.html_url"
          :href="repo.html_url"
          target="_blank"
          rel="noopener"
          class="text-body-1 font-weight-medium"
        >
          {{ repo.full_name }}
        </a>
        <span v-else class="text-body-1 font-weight-medium">{{ repo.full_name }}</span>
        <v-chip v-if="repo.visibility" size="x-small" variant="tonal">{{ repo.visibility }}</v-chip>
        <v-chip v-if="repo.language" size="x-small" variant="tonal">{{ repo.language }}</v-chip>
        <v-chip v-if="repo.default_branch" size="x-small" variant="tonal" prepend-icon="mdi-source-branch">
          {{ repo.default_branch }}
        </v-chip>
        <v-spacer />
        <span class="text-caption text-medium-emphasis">
          ★ {{ repo.stargazers_count ?? 0 }} · {{ repo.forks_count ?? 0 }} forks ·
          {{ repo.open_issues_count ?? 0 }} open issues
        </span>
      </div>
      <div v-if="repo.description" class="text-body-2 text-medium-emphasis mt-1">{{ repo.description }}</div>
    </v-sheet>

    <div class="text-body-2 text-medium-emphasis mb-3">
      Everything Factarium has replicated for this connection, held in the bronze tier. Basic fields are extracted into
      columns; open any row to see the raw source payload.
    </div>

    <div v-if="summary && summary.entityTypes.length === 0" class="text-medium-emphasis">
      No records synced yet. Run a sync from the connection's page first.
    </div>

    <template v-else-if="summary">
      <v-tabs v-model="selectedType" density="comfortable" class="mb-3">
        <v-tab v-for="t in summary.entityTypes" :key="t.entityType" :value="t.entityType">
          {{ t.entityType }} ({{ t.count }})
        </v-tab>
      </v-tabs>

      <!-- Filters -->
      <div class="d-flex align-center flex-wrap ga-3 mb-3">
        <VueDatePicker
          v-model="dateRange"
          :range="{ partialRange: false }"
          dark
          auto-apply
          :enable-time-picker="false"
          :max-date="maxDate"
          prevent-min-max-navigation
          format="yyyy-MM-dd"
          placeholder="Filter by date range"
          class="date-range"
        />
        <v-spacer />
        <v-select
          v-model="pageSize"
          :items="[25, 50, 100]"
          label="Per page"
          density="compact"
          hide-details
          style="max-width: 120px"
        />
        <v-btn
          v-if="selectedType"
          size="small"
          variant="tonal"
          color="red"
          prepend-icon="mdi-delete-sweep"
          @click="purgeOpen = true"
        >
          Purge {{ selectedType }}
        </v-btn>
      </div>

      <div v-if="loading" class="text-medium-emphasis text-caption py-2">Loading…</div>

      <template v-else>
        <!-- Commit-specific extracted columns -->
        <v-table v-if="selectedType === 'commit'" density="comfortable">
          <thead>
            <tr>
              <th style="width: 170px">Timestamp</th>
              <th style="width: 90px">Commit</th>
              <th>Message</th>
              <th style="width: 200px">Author(s)</th>
              <th style="width: 110px">Comments</th>
              <th style="width: 70px"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="records.length === 0">
              <td colspan="6" class="text-medium-emphasis text-caption py-4">No commits in this range.</td>
            </tr>
            <tr v-for="r in records" :key="r.id">
              <td class="text-caption">{{ fmt(r.sourceUpdatedAt) }}</td>
              <td>
                <a v-if="commit(r).link" :href="commit(r).link!" target="_blank" rel="noopener">
                  <code>{{ commit(r).shortHash }}</code>
                </a>
                <code v-else>{{ commit(r).shortHash }}</code>
              </td>
              <td>{{ commit(r).message }}</td>
              <td class="text-caption">{{ commit(r).authors.join(', ') || '—' }}</td>
              <td>
                <a v-if="commit(r).link" :href="commit(r).link!" target="_blank" rel="noopener">
                  {{ commit(r).commentCount }} <span aria-hidden="true">↗</span>
                </a>
                <span v-else class="text-medium-emphasis">—</span>
              </td>
              <td>
                <v-btn size="x-small" variant="text" @click="openRaw(r)">Raw</v-btn>
              </td>
            </tr>
          </tbody>
        </v-table>

        <!-- Pull-request-specific extracted columns -->
        <v-table v-else-if="selectedType === 'pull_request'" density="comfortable">
          <thead>
            <tr>
              <th style="width: 170px">Updated</th>
              <th style="width: 60px">PR</th>
              <th>Title</th>
              <th style="width: 100px">State</th>
              <th style="width: 150px">Author</th>
              <th style="width: 200px">Branch</th>
              <th style="width: 110px">Changes</th>
              <th style="width: 90px">Comments</th>
              <th style="width: 70px"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="records.length === 0">
              <td colspan="9" class="text-medium-emphasis text-caption py-4">No pull requests in this range.</td>
            </tr>
            <tr v-for="r in records" :key="r.id">
              <td class="text-caption">{{ fmt(r.sourceUpdatedAt) }}</td>
              <td>
                <a v-if="pr(r).link" :href="pr(r).link!" target="_blank" rel="noopener">
                  <code>#{{ pr(r).number }}</code>
                </a>
                <code v-else>#{{ pr(r).number }}</code>
              </td>
              <td>{{ pr(r).title }}</td>
              <td>
                <v-chip size="x-small" variant="flat" :color="prStateColor[pr(r).state] || 'grey'">
                  {{ pr(r).state }}
                </v-chip>
              </td>
              <td class="text-caption">{{ pr(r).author ?? '—' }}</td>
              <td class="text-caption">
                <code>{{ pr(r).headRef ?? '?' }}</code> →
                <code>{{ pr(r).baseRef ?? '?' }}</code>
              </td>
              <td class="text-caption">
                <span v-if="pr(r).additions !== null" class="text-success">+{{ pr(r).additions }}</span>
                <span v-if="pr(r).deletions !== null" class="text-error"> −{{ pr(r).deletions }}</span>
                <span v-if="pr(r).additions === null && pr(r).deletions === null" class="text-medium-emphasis">—</span>
              </td>
              <td>{{ pr(r).comments }}</td>
              <td>
                <v-btn size="x-small" variant="text" @click="openRaw(r)">Raw</v-btn>
              </td>
            </tr>
          </tbody>
        </v-table>

        <!-- Issue-specific extracted columns (the id is dropped; the key links to Jira). -->
        <v-table v-else-if="selectedType === 'issue'" density="comfortable">
          <thead>
            <tr>
              <th style="width: 110px">Key</th>
              <th>Title</th>
              <th style="width: 150px">Status</th>
              <th style="width: 80px">Points</th>
              <th style="width: 160px">Primary dev</th>
              <th style="width: 200px">Sprints</th>
              <th style="width: 100px">Comments</th>
              <th style="width: 70px"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="records.length === 0">
              <td colspan="8" class="text-medium-emphasis text-caption py-4">No issues in this range.</td>
            </tr>
            <tr v-for="r in records" :key="r.id">
              <td>
                <a v-if="issue(r).link" :href="issue(r).link!" target="_blank" rel="noopener">
                  <code>{{ issue(r).key }}</code>
                </a>
                <code v-else>{{ issue(r).key }}</code>
              </td>
              <td>{{ issue(r).title }}</td>
              <td>
                <v-chip
                  v-if="issue(r).status"
                  size="x-small"
                  variant="flat"
                  :color="issueStatusColor[issue(r).statusCategory ?? ''] || 'grey'"
                >
                  {{ issue(r).status }}
                </v-chip>
                <span v-else class="text-medium-emphasis">—</span>
              </td>
              <td>
                <span v-if="issue(r).storyPoints !== null">{{ issue(r).storyPoints }}</span>
                <span v-else class="text-medium-emphasis">—</span>
              </td>
              <td class="text-caption">{{ issue(r).primaryDeveloper || '—' }}</td>
              <td class="text-caption">{{ issue(r).sprints || '—' }}</td>
              <td>{{ issue(r).commentCount }}</td>
              <td>
                <v-btn size="x-small" variant="text" @click="openRaw(r)">Raw</v-btn>
              </td>
            </tr>
          </tbody>
        </v-table>

        <!-- Sprint-specific extracted columns. -->
        <v-table v-else-if="selectedType === 'sprint'" density="comfortable">
          <thead>
            <tr>
              <th>Name</th>
              <th style="width: 150px">Velocity report</th>
              <th style="width: 220px">Range</th>
              <th style="width: 120px">State</th>
              <th style="width: 70px"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="records.length === 0">
              <td colspan="5" class="text-medium-emphasis text-caption py-4">No sprints in this range.</td>
            </tr>
            <tr v-for="r in records" :key="r.id">
              <td>
                <a v-if="sprint(r).link" :href="sprint(r).link!" target="_blank" rel="noopener">{{ sprint(r).name }}</a>
                <span v-else>{{ sprint(r).name }}</span>
              </td>
              <td>
                <a v-if="sprint(r).velocityLink" :href="sprint(r).velocityLink!" target="_blank" rel="noopener">
                  Velocity <span aria-hidden="true">↗</span>
                </a>
                <span v-else class="text-medium-emphasis">—</span>
              </td>
              <td class="text-caption">{{ sprint(r).range || '—' }}</td>
              <td>
                <v-chip
                  v-if="sprint(r).state"
                  size="x-small"
                  variant="flat"
                  :color="sprintStateColor[sprint(r).state ?? ''] || 'grey'"
                >
                  {{ sprint(r).state }}
                </v-chip>
                <span v-else class="text-medium-emphasis">—</span>
              </td>
              <td>
                <v-btn size="x-small" variant="text" @click="openRaw(r)">Raw</v-btn>
              </td>
            </tr>
          </tbody>
        </v-table>

        <!-- Changelog summary: each row is one issue's flow, summarised from its history. -->
        <v-table v-else-if="selectedType === 'issue_changelog'" density="comfortable">
          <thead>
            <tr>
              <th style="width: 120px">Issue</th>
              <th>Flow</th>
              <th style="width: 80px">Moves</th>
              <th style="width: 220px">Churn</th>
              <th style="width: 170px">Last activity</th>
              <th style="width: 70px"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="records.length === 0">
              <td colspan="6" class="text-medium-emphasis text-caption py-4">No changelogs in this range.</td>
            </tr>
            <tr v-for="r in records" :key="r.id">
              <td><code>{{ changelog(r).issueId }}</code></td>
              <td class="text-caption flow-path">{{ changelog(r).statusPath || '—' }}</td>
              <td>{{ changelog(r).transitions }}</td>
              <td>
                <div class="d-flex flex-wrap ga-1">
                  <v-chip
                    v-if="changelog(r).reassignments"
                    size="x-small"
                    variant="tonal"
                    prepend-icon="mdi-account-switch"
                    title="Reassignments"
                  >
                    {{ changelog(r).reassignments }}
                  </v-chip>
                  <v-chip
                    v-if="changelog(r).sprintChanges"
                    size="x-small"
                    variant="tonal"
                    prepend-icon="mdi-calendar-sync"
                    title="Sprint changes (scope moved in/out)"
                  >
                    {{ changelog(r).sprintChanges }}
                  </v-chip>
                  <v-chip
                    v-if="changelog(r).reestimations"
                    size="x-small"
                    variant="tonal"
                    prepend-icon="mdi-scale-balance"
                    title="Story-point re-estimations"
                  >
                    {{ changelog(r).reestimations }}
                  </v-chip>
                  <v-chip
                    v-if="changelog(r).reopens"
                    size="x-small"
                    variant="tonal"
                    color="orange"
                    prepend-icon="mdi-restore"
                    title="Reopens (resolution cleared)"
                  >
                    {{ changelog(r).reopens }}
                  </v-chip>
                  <v-chip
                    v-if="changelog(r).everBlocked"
                    size="x-small"
                    variant="tonal"
                    color="red"
                    prepend-icon="mdi-flag"
                    title="Was flagged as blocked at some point"
                  >
                    Blocked
                  </v-chip>
                  <span
                    v-if="
                      !changelog(r).reassignments &&
                      !changelog(r).sprintChanges &&
                      !changelog(r).reestimations &&
                      !changelog(r).reopens &&
                      !changelog(r).everBlocked
                    "
                    class="text-medium-emphasis"
                    >—</span
                  >
                </div>
              </td>
              <td class="text-caption">{{ fmt(changelog(r).lastActivity) }}</td>
              <td>
                <v-btn size="x-small" variant="text" @click="openRaw(r)">Raw</v-btn>
              </td>
            </tr>
          </tbody>
        </v-table>

        <!-- Generic view for other entity types -->
        <v-table v-else density="comfortable">
          <thead>
            <tr>
              <th>Source id</th>
              <th style="width: 200px">Updated</th>
              <th style="width: 90px">Version</th>
              <th style="width: 70px"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="records.length === 0">
              <td colspan="4" class="text-medium-emphasis text-caption py-4">No records in this range.</td>
            </tr>
            <tr v-for="r in records" :key="r.id">
              <td>{{ r.sourceId }}</td>
              <td class="text-caption">{{ fmt(r.sourceUpdatedAt) }}</td>
              <td>{{ r.version }}</td>
              <td>
                <v-btn size="x-small" variant="text" @click="openRaw(r)">Raw</v-btn>
              </td>
            </tr>
          </tbody>
        </v-table>

        <div class="d-flex align-center justify-space-between mt-3">
          <span class="text-caption text-medium-emphasis">{{ total }} total</span>
          <v-pagination
            v-if="pageCount > 1"
            :model-value="page"
            :length="pageCount"
            :total-visible="7"
            density="comfortable"
            @update:model-value="goToPage"
          />
        </div>
      </template>
    </template>

    <!-- Purge confirmation -->
    <v-dialog v-model="purgeOpen" max-width="480">
      <v-card>
        <v-card-title class="pt-4">Purge {{ selectedType }} records?</v-card-title>
        <v-card-text>
          This permanently deletes all <strong>{{ selectedType }}</strong> records for this connection ({{
            selectedTypeCount
          }}
          rows) and resets its sync position, so the next sync re-fetches them from scratch. This can't be undone.
        </v-card-text>
        <v-card-actions class="px-4 pb-4">
          <v-spacer />
          <v-btn variant="text" @click="purgeOpen = false">Cancel</v-btn>
          <v-btn color="red" variant="tonal" :loading="purging" @click="purge">Purge</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- Raw payload modal -->
    <v-dialog v-model="rawOpen" max-width="760">
      <v-card>
        <v-card-title class="d-flex align-center">
          <span>Raw record</span>
          <v-spacer />
          <v-btn icon="mdi-close" variant="text" size="small" @click="rawOpen = false" />
        </v-card-title>
        <v-card-text>
          <div v-if="rawRecord" class="text-caption text-medium-emphasis mb-2">
            {{ rawRecord.entityType }} · {{ rawRecord.sourceId }} · v{{ rawRecord.version }} · fetched
            {{ fmt(rawRecord.fetchedAt) }}
          </div>
          <pre class="payload">{{ rawRecord ? pretty(rawRecord.payload) : '' }}</pre>
        </v-card-text>
      </v-card>
    </v-dialog>
  </BasePage>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { VueDatePicker } from '@vuepic/vue-datepicker';
import '@vuepic/vue-datepicker/dist/main.css';
import BasePage from '../../components/BasePage.vue';
import type { RecordsSummary, RawRecordView, RecordsPageResult } from '../../types';
import { formatDateTime as fmt } from '../../utils/datetime';
import { api } from '../../api';

const route = useRoute();
const id = computed(() => String(route.params.id));

const summary = ref<RecordsSummary | null>(null);
const records = ref<RawRecordView[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(25);
const selectedType = ref<string | null>(null);
// vue-datepicker range: [start, end], or null when cleared.
const dateRange = ref<Date[] | null>(null);
// Records only exist in the past, so disallow selecting into the future.
const maxDate = new Date();
const error = ref<string | null>(null);
const loading = ref(false);

const rawOpen = ref(false);
const rawRecord = ref<RawRecordView | null>(null);

const purgeOpen = ref(false);
const purging = ref(false);
const selectedTypeCount = computed(
  () => summary.value?.entityTypes.find((t) => t.entityType === selectedType.value)?.count ?? 0
);

const title = computed(() => (summary.value ? `${summary.value.name} — records` : 'Records'));
const repo = computed(() => summary.value?.repository ?? null);
const pageCount = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)));

const backTo = computed(() => {
  switch (summary.value?.type) {
    case 'github':
      return '/integrations/github';
    case 'jira':
      return '/integrations/jira';
    case 'tempo':
      return '/integrations/tempo';
    case 'claude-code':
      return '/integrations/claude';
    default:
      return '/integrations';
  }
});

async function loadSummary() {
  try {
    summary.value = await api.url(`/integrations/${id.value}/records/summary`).get().json<RecordsSummary>();
    error.value = null;
    const types = summary.value?.entityTypes ?? [];
    selectedType.value = types.length > 0 ? types[0].entityType : null;
  } catch (e) {
    error.value = String(e);
  }
}

async function loadRecords() {
  if (!selectedType.value) {
    records.value = [];
    total.value = 0;
    return;
  }
  loading.value = true;
  try {
    const params = new URLSearchParams({
      entityType: selectedType.value,
      page: String(page.value),
      pageSize: String(pageSize.value)
    });
    const [from, to] = dateRange.value ?? [];
    if (from) params.set('from', startOfDay(from).toISOString());
    if (to) params.set('to', endOfDay(to).toISOString());

    const result = await api.url(`/integrations/${id.value}/records?${params}`).get().json<RecordsPageResult>();
    records.value = result.records;
    total.value = result.total;
    error.value = null;
  } catch (e) {
    error.value = String(e);
  } finally {
    loading.value = false;
  }
}

async function reload() {
  await loadSummary();
  await loadRecords();
}

function goToPage(p: number) {
  page.value = p;
  loadRecords();
}

// Inclusive day bounds in local time, matching the picker's date-only selection.
function startOfDay(d: Date): Date {
  const x = new Date(d);
  x.setHours(0, 0, 0, 0);
  return x;
}

function endOfDay(d: Date): Date {
  const x = new Date(d);
  x.setHours(23, 59, 59, 999);
  return x;
}

// Any tab / filter / page-size change resets to page 1 and reloads.
watch([selectedType, dateRange, pageSize], () => {
  page.value = 1;
  loadRecords();
});

function openRaw(r: RawRecordView) {
  rawRecord.value = r;
  rawOpen.value = true;
}

// Deletes every record of the selected entity type and resets its sync cursor, then
// reloads (the purged tab drops and selection falls back to the first remaining type).
async function purge() {
  if (!selectedType.value) return;
  purging.value = true;
  try {
    await api
      .url(`/integrations/${id.value}/records?entityType=${encodeURIComponent(selectedType.value)}`)
      .delete()
      .res();
    purgeOpen.value = false;
    await reload();
  } catch (e) {
    error.value = String(e);
  } finally {
    purging.value = false;
  }
}

// --- commit column extraction ---
interface CommitRow {
  shortHash: string;
  message: string;
  authors: string[];
  link: string | null;
  commentCount: number;
}

function commit(r: RawRecordView): CommitRow {
  const p = (r.payload ?? {}) as Record<string, any>;
  const sha: string = p.sha ?? r.sourceId ?? '';
  const repo: string = p.repo ?? '';
  const message: string = String(p.message ?? '').split('\n')[0];
  return {
    shortHash: sha.slice(0, 7),
    message,
    authors: extractAuthors(p),
    // The commit page (where comments live) is derivable from repo + sha, so we
    // don't need to store a url.
    link: repo && sha ? `https://github.com/${repo}/commit/${sha}` : null,
    commentCount: p.comment_count ?? 0
  };
}

function extractAuthors(p: Record<string, any>): string[] {
  const names: string[] = [];
  // GitHub attributes a commit to its author and shows the account handle when it
  // resolved one, falling back to the git name. committer_* covers records synced
  // before the author switch.
  const primary = p.author_login ?? p.author_name ?? p.committer_login ?? p.committer_name;
  if (primary) names.push(String(primary));

  const coAuthors: any[] = Array.isArray(p.co_authors) ? p.co_authors : [];
  if (coAuthors.length > 0) {
    // Structured co-authors extracted at sync time.
    for (const ca of coAuthors) {
      const name = ca?.name || ca?.email;
      if (name && !names.includes(String(name))) names.push(String(name));
    }
  } else {
    // Fallback: parse Co-authored-by trailers from the message (older records).
    const message = String(p.message ?? '');
    const re = /Co-authored-by:\s*([^<\n]+?)\s*(?:<[^>]*>)?\s*$/gim;
    let m: RegExpExecArray | null;
    while ((m = re.exec(message)) !== null) {
      const name = m[1].trim();
      if (name && !names.includes(name)) names.push(name);
    }
  }
  return names;
}

// --- pull-request column extraction ---
interface PrRow {
  number: number;
  title: string;
  state: string;
  author: string | null;
  headRef: string | null;
  baseRef: string | null;
  additions: number | null;
  deletions: number | null;
  comments: number;
  link: string | null;
}

const prStateColor: Record<string, string> = {
  open: 'green',
  merged: 'purple',
  closed: 'red'
};

function pr(r: RawRecordView): PrRow {
  const p = (r.payload ?? {}) as Record<string, any>;
  const repo: string = p.repository_full_name ?? '';
  const number: number = p.number ?? 0;
  return {
    number,
    title: String(p.title ?? ''),
    // GitHub reports merged PRs as "closed"; surface merged as its own state.
    state: p.merged_at ? 'merged' : (p.state ?? 'unknown'),
    author: p.author_login ?? null,
    headRef: p.head_ref ?? null,
    baseRef: p.base_ref ?? null,
    additions: p.additions ?? null,
    deletions: p.deletions ?? null,
    comments: (p.comment_count ?? 0) + (p.review_comment_count ?? 0),
    link: repo && number ? `https://github.com/${repo}/pull/${number}` : null
  };
}

// --- issue column extraction ---
interface IssueRow {
  key: string;
  title: string;
  status: string | null;
  statusCategory: string | null;
  storyPoints: number | null;
  primaryDeveloper: string | null;
  sprints: string;
  commentCount: number;
  link: string | null;
}

// Jira status categories: new (To Do), indeterminate (In Progress), done (Done).
const issueStatusColor: Record<string, string> = {
  new: 'grey',
  indeterminate: 'blue',
  done: 'green'
};

function issue(r: RawRecordView): IssueRow {
  const p = (r.payload ?? {}) as Record<string, any>;
  const key: string = p.key ?? r.sourceId ?? '';
  const sprints: any[] = Array.isArray(p.sprints) ? p.sprints : [];
  const site = summary.value?.siteUrl?.replace(/\/$/, '');
  return {
    key,
    title: String(p.title ?? ''),
    status: p.status ?? null,
    statusCategory: p.status_category_key ?? null,
    storyPoints: typeof p.story_points === 'number' ? p.story_points : null,
    primaryDeveloper: p.primary_developer_name ?? null,
    sprints: sprints
      .map((s) => s?.name)
      .filter(Boolean)
      .join(', '),
    commentCount: p.comment_count ?? 0,
    link: site && key ? `${site}/browse/${key}` : null
  };
}

// --- sprint column extraction ---
interface SprintRow {
  name: string;
  state: string | null;
  range: string;
  link: string | null; // the sprint's board
  velocityLink: string | null; // sprint retrospective/velocity report
}

const sprintStateColor: Record<string, string> = {
  active: 'green',
  closed: 'grey',
  future: 'blue'
};

function sprint(r: RawRecordView): SprintRow {
  const p = (r.payload ?? {}) as Record<string, any>;
  const site = summary.value?.siteUrl?.replace(/\/$/, '');
  const projectKey = summary.value?.projectKey;
  const boardId = p.board_id ?? null;
  const id = p.id ?? r.sourceId;
  // Board and report URLs need the site, project, and board; skip the link if any is missing.
  const boardUrl =
    site && projectKey && boardId ? `${site}/jira/software/c/projects/${projectKey}/boards/${boardId}` : null;
  const day = (v: unknown) => (typeof v === 'string' ? v.slice(0, 10) : null);
  const [start, end] = [day(p.start_date), day(p.end_date)];
  return {
    name: String(p.name ?? r.sourceId ?? ''),
    state: p.state ?? null,
    range: start && end ? `${start} → ${end}` : (start ?? end ?? ''),
    link: boardUrl,
    velocityLink: boardUrl && id ? `${boardUrl}/reports/sprint-retrospective?sprint=${id}` : null
  };
}

// --- changelog summary extraction ---
// One issue_changelog record is an issue's whole history; we summarise the compact
// { entries: [{ at, author_name, changes: [{ field, from_str, to_str, to }] }] } projection
// (see JiraPullSource) into a per-issue flow snapshot.
interface ChangelogRow {
  issueId: string;
  statusPath: string;
  transitions: number;
  reassignments: number;
  sprintChanges: number;
  reestimations: number;
  reopens: number;
  everBlocked: boolean;
  lastActivity: string | null;
}

function changelog(r: RawRecordView): ChangelogRow {
  const p = (r.payload ?? {}) as Record<string, any>;
  const entries: any[] = Array.isArray(p.entries) ? p.entries : [];
  // Stored in fetch order; sort by timestamp so the flow path reads chronologically.
  const sorted = [...entries].sort((a, b) => String(a?.at ?? '').localeCompare(String(b?.at ?? '')));
  const allChanges = sorted.flatMap((e) => (Array.isArray(e?.changes) ? e.changes : []));

  const field = (c: any) => String(c?.field ?? '').toLowerCase();
  const changesOf = (name: string) => allChanges.filter((c) => field(c) === name.toLowerCase());
  const isStoryPoints = (c: any) => field(c) === 'story points' || field(c) === 'story point estimate';

  // The status path: the first transition's origin, then every landing status in order.
  const statusChanges = changesOf('status');
  const path: string[] = [];
  statusChanges.forEach((c: any, i: number) => {
    if (i === 0 && c.from_str) path.push(String(c.from_str));
    if (c.to_str) path.push(String(c.to_str));
  });

  return {
    issueId: String(p.issueId ?? r.sourceId ?? ''),
    statusPath: path.join(' → '),
    transitions: statusChanges.length,
    reassignments: changesOf('assignee').length,
    sprintChanges: changesOf('Sprint').length,
    reestimations: allChanges.filter(isStoryPoints).length,
    // A cleared resolution is Jira's signal that an issue was reopened.
    reopens: changesOf('resolution').filter((c: any) => !c.to).length,
    everBlocked: changesOf('Flagged').some((c: any) => c.to_str),
    lastActivity: sorted.length ? (sorted[sorted.length - 1].at ?? null) : null
  };
}

function pretty(payload: unknown) {
  return JSON.stringify(payload, null, 2);
}

onMounted(loadSummary);
</script>

<style scoped>
.payload {
  background: rgba(255, 255, 255, 0.06);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  padding: 12px;
  font-size: 0.78rem;
  line-height: 1.4;
  overflow-x: auto;
  white-space: pre;
  max-height: 60vh;
}

/* Changelog flow path: wrap across lines so the full status journey stays readable. */
.flow-path {
  white-space: normal;
  overflow-wrap: anywhere;
  line-height: 1.5;
  padding-top: 6px;
  padding-bottom: 6px;
}

/* vue-datepicker: sized and rounded to sit alongside the compact Vuetify fields. */
.date-range {
  max-width: 280px;
}
.date-range :deep(.dp__input) {
  font-size: 0.875rem;
  border-radius: 4px;
}
</style>
