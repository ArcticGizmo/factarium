<script setup>
import { ref, onMounted } from 'vue'

const emit = defineEmits(['changed'])

const identities = ref([])
const people = ref([])
const newPersonName = ref('')
const selectedPerson = ref({})
const error = ref(null)

async function load() {
  error.value = null
  try {
    ;[identities.value, people.value] = await Promise.all([
      fetch('/api/identities').then((r) => r.json()),
      fetch('/api/people').then((r) => r.json()),
    ])
  } catch (e) {
    error.value = String(e)
  }
}

async function createPerson() {
  const name = newPersonName.value.trim()
  if (!name) return
  await fetch('/api/people', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ displayName: name }),
  })
  newPersonName.value = ''
  await load()
}

async function link(identityId) {
  const personId = selectedPerson.value[identityId]
  if (!personId) return
  await fetch(`/api/identities/${identityId}/link`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ personId }),
  })
  await afterChange()
}

async function unlink(identityId) {
  await fetch(`/api/identities/${identityId}/unlink`, { method: 'POST' })
  await afterChange()
}

async function afterChange() {
  await load()
  emit('changed')
}

const personOptions = () => people.value.map((p) => ({ title: p.displayName, value: p.id }))

onMounted(load)
</script>

<template>
  <v-card title="People & identity mapping">
    <template #append>
      <v-btn size="small" variant="text" @click="load">Refresh</v-btn>
    </template>
    <v-card-text>
      <div v-if="error" class="text-error mb-2">{{ error }}</div>

      <div class="d-flex align-center ga-2 mb-4" style="max-width: 460px">
        <v-text-field
          v-model="newPersonName"
          label="New person name"
          density="compact"
          hide-details
          @keyup.enter="createPerson"
        />
        <v-btn color="primary" variant="tonal" @click="createPerson">Add</v-btn>
      </div>

      <v-table density="comfortable">
        <thead>
          <tr>
            <th>Identity</th>
            <th>Source</th>
            <th>Mapped to</th>
            <th style="width: 320px">Map</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="i in identities" :key="i.id">
            <td>{{ i.login }}</td>
            <td>{{ i.source }}</td>
            <td>
              <v-chip v-if="i.personName" color="green" size="x-small" variant="flat">
                {{ i.personName }}
              </v-chip>
              <span v-else class="text-medium-emphasis">unmapped</span>
            </td>
            <td>
              <div class="d-flex align-center ga-2">
                <v-select
                  v-model="selectedPerson[i.id]"
                  :items="personOptions()"
                  label="Person"
                  density="compact"
                  hide-details
                  style="max-width: 200px"
                />
                <v-btn size="x-small" color="primary" variant="tonal" @click="link(i.id)">Link</v-btn>
                <v-btn v-if="i.personId" size="x-small" variant="text" @click="unlink(i.id)">
                  Unlink
                </v-btn>
              </div>
            </td>
          </tr>
        </tbody>
      </v-table>
    </v-card-text>
  </v-card>
</template>
