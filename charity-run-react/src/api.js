// STUDENT NOTE:
// Change this value if your backend runs on a different port.
export const API_BASE = 'http://localhost:5000'

export async function getJson(path, token) {
  const response = await fetch(`${API_BASE}${path}`, {
    headers: token
      ? {
          Authorization: `Bearer ${token}`,
        }
      : undefined,
  })

  return response.json()
}

export async function postJson(path, body, token) {
  const response = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify(body),
  })

  const text = await response.text()
  try {
      return text ? JSON.parse(text) : {}
  } catch {
      return {}
  }
}
  // return response.json()

export async function login(email, password) {
  return postJson('/api/auth/login', { email, password })
}

export async function registerRunner(data) {
  return postJson('/api/auth/register', data)
}

export async function getPublicCharity() {
  return getJson('/api/public/charity')
}

export async function getPublicRace() {
  return getJson('/api/public/race')
}

export async function getPublicSchedule() {
  return getJson('/api/public/schedule')
}

export async function getMyProfile(token) {
  return getJson('/api/runners/me', token)
}

export async function getMyTeammates(token) {
  return getJson('/api/runners/me/teammates', token)
}

export async function getAdminRunners(token) {
  return getJson('/api/admin/runners', token)
}
