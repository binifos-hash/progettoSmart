import React, { useEffect, useState } from 'react'
import { fetchRequests, approveRequest, rejectRequest, deleteRequest } from '../api'

function normalize(req) {
  const id = req.id ?? req.Id
  const status = req.status ?? req.Status ?? ''
  const employeeName = req.employeeName ?? req.EmployeeName ?? req.EmployeeUsername ?? ''
  // Normalize date to YYYY-MM-DD string
  const rawDate = req.date ?? req.Date ?? ''
  let date = ''
  try {
    if (typeof rawDate === 'string') {
      if (/^\d{4}-\d{2}-\d{2}$/.test(rawDate)) date = rawDate
      else date = rawDate.slice(0,10)
    } else if (rawDate instanceof Date) {
      date = rawDate.toISOString().slice(0,10)
    } else {
      date = ''
    }
  } catch { date = '' }
  return { id, status, employeeName, date, raw: req }
}

function formatDateLocal(yyyyMMdd) {
  if (!yyyyMMdd) return ''
  const m = yyyyMMdd.match(/^(\d{4})-(\d{2})-(\d{2})$/)
  if (!m) return new Date(yyyyMMdd).toLocaleDateString()
  const y = parseInt(m[1],10), mo = parseInt(m[2],10)-1, d = parseInt(m[3],10)
  return new Date(y, mo, d).toLocaleDateString()
}

export default function ManagerView({ reloadSignal = 0 }) {
  const [items, setItems] = useState([])
  async function load() {
    const r = await fetchRequests()
    const norm = (r || []).map(normalize)
    setItems(norm)
  }

  useEffect(() => { load() }, [reloadSignal])

  async function approve(id) {
    try {
      const res = await approveRequest(id)
      setItems(prev => prev.map(it => it.id === id ? ({ ...it, status: res.status ?? res.Status ?? 'Approved' }) : it))
    } catch (e) {
      // fallback: reload full list
      await load()
      return
    }
    // ensure full sync
    await load()
  }

  async function reject(id) {
    try {
      const res = await rejectRequest(id)
      setItems(prev => prev.map(it => it.id === id ? ({ ...it, status: res.status ?? res.Status ?? 'Rejected' }) : it))
    } catch (e) {
      await load()
      return
    }
    await load()
  }

  async function remove(id) {
    if (!confirm('Confermi di eliminare questa richiesta?')) return
    try {
      await deleteRequest(id)
    } catch (e) {
      console.error(e)
    }
    await load()
  }

  function getStatusBadge(status) {
    const s = status || ''
    const classes = `badge ${s.toLowerCase()}`
    const labels = { Approved: '✓ Approved', Rejected: '✕ Rejected', Pending: '⊙ Pending' }
    return <span className={classes}>{labels[status] || status}</span>
  }

  return (
    <div className="card">
      <h2>Richieste</h2>
      <table className="requests">
        <thead>
          <tr><th>Dipendente</th><th>Data</th><th>Stato</th><th>Azione</th><th></th></tr>
        </thead>
        <tbody>
          {items.map(x => (
            <tr key={x.id}>
              <td>{x.employeeName}</td>
              <td>{formatDateLocal(x.date)}</td>
              <td>{getStatusBadge(x.status)}</td>
              <td>
                {x.status === 'Pending' ? (
                  <div className="actions">
                    <button className="btn primary" onClick={() => approve(x.id)}>Accetta</button>
                    <button className="btn secondary" onClick={() => reject(x.id)}>Rifiuta</button>
                  </div>
                ) : (
                  <div className="actions">
                    <strong>{x.status}</strong>
                  </div>
                )}
              </td>
              <td className="col-delete">
                <button className="btn danger" onClick={() => remove(x.id)} aria-label="Elimina richiesta">
                  <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <polyline points="3 6 5 6 21 6" />
                    <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
                    <line x1="10" y1="11" x2="10" y2="17" />
                    <line x1="14" y1="11" x2="14" y2="17" />
                    <path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                  </svg>
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
