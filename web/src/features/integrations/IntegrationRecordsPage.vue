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
          range
          dark
          auto-apply
          :enable-time-picker="false"
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
const error = ref<string | null>(null);
const loading = ref(false);

const rawOpen = ref(false);
const rawRecord = ref<RawRecordView | null>(null);

const title = computed(() => (summary.value ? `${summary.value.name} — records` : 'Records'));
const repo = computed(() => summary.value?.repository ?? null);
const pageCount = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)));

const backTo = computed(() => {
  switch (summary.value?.type) {
    case 'github':
      return '/integrations/github';
    case 'jira':
      return '/integrations/jira';
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

/* vue-datepicker: sized and rounded to sit alongside the compact Vuetify fields. */
.date-range {
  max-width: 280px;
}
.date-range :deep(.dp__input) {
  font-size: 0.875rem;
  border-radius: 4px;
}
</style>
