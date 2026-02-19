const BASE = import.meta.env.VITE_API_BASE || 'http://localhost:5000'

function authHeaders() {
  const token = localStorage.getItem('sw_token')
  return token ? { 'Authorization': `Bearer ${token}` } : {}
}

export async function login(username, password) {
  const res = await fetch(`${BASE}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, password })
  })
  if (!res.ok) throw new Error('Invalid credentials')
  return res.json()
}

export async function me() {
  const res = await fetch(`${BASE}/me`, { headers: authHeaders() })
  if (!res.ok) return null
  return res.json()
}

export async function updateTheme(theme) {
  const res = await fetch(`${BASE}/me/theme`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify({ theme })
  })
  return res.json()
}

export async function createRequest(payload) {
  const res = await fetch(`${BASE}/requests`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify(payload)
  })
  return res.json()
}

export async function fetchRequests() {
  const res = await fetch(`${BASE}/requests`, { headers: authHeaders() })
  return res.json()
}

export async function fetchMyRequests() {
  const res = await fetch(`${BASE}/requests/mine`, { headers: authHeaders() })
  return res.json()
}

export async function approveRequest(id) {
  const res = await fetch(`${BASE}/requests/${id}/approve`, { method: 'POST', headers: authHeaders() })
  return res.json()
}

export async function rejectRequest(id) {
  const res = await fetch(`${BASE}/requests/${id}/reject`, { method: 'POST', headers: authHeaders() })
  return res.json()
}

export async function fetchRecurringRequests() {
  const res = await fetch(`${BASE}/recurring-requests`, { headers: authHeaders() })
  return res.json()
}

export async function fetchMyRecurringRequests() {
  const res = await fetch(`${BASE}/recurring-requests/mine`, { headers: authHeaders() })
  return res.json()
}

export async function createRecurringRequest(dayOfWeek, dayName) {
  const res = await fetch(`${BASE}/recurring-requests`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify({ dayOfWeek, dayName })
  })
  return res.json()
}

export async function approveRecurringRequest(id) {
  const res = await fetch(`${BASE}/recurring-requests/${id}/approve`, { method: 'POST', headers: authHeaders() })
  return res.json()
}

export async function rejectRecurringRequest(id) {
  const res = await fetch(`${BASE}/recurring-requests/${id}/reject`, { method: 'POST', headers: authHeaders() })
  return res.json()
}

export async function deleteRequest(id) {
  const res = await fetch(`${BASE}/requests/${id}`, { method: 'DELETE', headers: authHeaders() })
  if (!res.ok) throw new Error('Delete failed')
  return res.json()
}

export async function deleteRecurringRequest(id) {
  const res = await fetch(`${BASE}/recurring-requests/${id}`, { method: 'DELETE', headers: authHeaders() })
  if (!res.ok) throw new Error('Delete failed')
  return res.json()
}

export async function forgotPassword(email) {
  const res = await fetch(`${BASE}/auth/forgot-password`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email })
  })
  if (!res.ok) throw new Error('Not found')
  return res.json()
}

export async function fetchUsers() {
  const res = await fetch(`${BASE}/users`, { headers: authHeaders() })
  if (!res.ok) throw new Error('Unauthorized')
  return res.json()
}

export async function createUser(payload) {
  const res = await fetch(`${BASE}/users`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify(payload)
  })
  if (!res.ok) {
    const txt = await res.text()
    throw new Error(txt || 'Create user failed')
  }
  return res.json()
}

export async function deleteUser(username) {
  const res = await fetch(`${BASE}/users/${encodeURIComponent(username)}`, { method: 'DELETE', headers: authHeaders() })
  if (!res.ok) {
    const txt = await res.text()
    throw new Error(txt || 'Delete user failed')
  }
  return res.json()
}

export async function changePassword(oldPassword, newPassword) {
  const body = { newPassword };
  if (oldPassword) body.oldPassword = oldPassword;
  const res = await fetch(`${BASE}/me/change-password`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify(body)
  })
  if (!res.ok) throw new Error('Change password failed')
  return res.json()
}
