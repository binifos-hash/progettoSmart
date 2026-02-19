import React, { useEffect, useState } from 'react'
import ManagerView from './ManagerView'
import Calendar from './Calendar'
import { fetchRequests, fetchRecurringRequests, approveRecurringRequest, rejectRecurringRequest, deleteRecurringRequest, fetchUsers, createUser, deleteUser } from '../api'

export default function AdminView(){
  const [tab, setTab] = useState('requests')
  const [requests, setRequests] = useState([])
  const [recurringRequests, setRecurringRequests] = useState([])
  const [users, setUsers] = useState([])
  const [createErr, setCreateErr] = useState(null)
  const [createMsg, setCreateMsg] = useState(null)
  const [newUser, setNewUser] = useState({ username: '', displayName: '', email: '', role: 'Employee' })
  const [reloadSignal, setReloadSignal] = useState(0)

  async function load(){
    const r = await fetchRequests()
    setRequests(r)
    const rr = await fetchRecurringRequests()
    setRecurringRequests(rr)
    try {
      const u = await fetchUsers()
      setUsers(u)
    } catch (e) { setUsers([]) }
    setReloadSignal(s => s + 1)
  }

  useEffect(()=>{ load() }, [])

  async function approveRecurring(id) {
    await approveRecurringRequest(id)
    await load()
  }

  async function rejectRecurring(id) {
    await rejectRecurringRequest(id)
    await load()
  }

  async function removeRecurring(id) {
    if (!confirm('Confermi di eliminare questa richiesta ricorsiva?')) return
    try {
      await deleteRecurringRequest(id)
    } catch (e) { console.error(e) }
    await load()
  }

  async function removeUser(username) {
    if (!confirm(`Confermi di eliminare l'utente ${username}?`)) return
    try {
      await deleteUser(username)
    } catch (e) { alert('Errore eliminazione utente: ' + (e.message || e)); return }
    await load()
  }

  function getStatusBadge(status) {
    const classes = `badge ${status.toLowerCase()}`
    const labels = { Approved: '✓ Approved', Rejected: '✕ Rejected', Pending: '⊙ Pending' }
    return <span className={classes}>{labels[status] || status}</span>
  }

  const approved = (requests||[])
    .filter(x => (x.status || x.Status) === 'Approved')
    .map(x => ({ date: (x.date || x.Date).slice(0,10), title: x.employeeName || x.EmployeeName }))

  const approvedRecurringRequests = (recurringRequests||[])
    .filter(r => (r.status || r.Status) === 'Approved')
    .map(r => ({ dayOfWeek: r.dayOfWeek ?? r.DayOfWeek, title: r.employeeName || r.EmployeeName }))

  return (
    <div className="card">
      <div className="tabs admin-tabs">
        <button className={tab==='requests' ? 'active' : ''} onClick={()=>setTab('requests')}>Richieste</button>
        <button className={tab==='recurring' ? 'active' : ''} onClick={()=>{ setTab('recurring'); load(); }}>Ricorsivo</button>
        <button className={tab==='personnel' ? 'active' : ''} onClick={()=>{ setTab('personnel'); load(); }}>Personale</button>
        <button className={tab==='calendar' ? 'active' : ''} onClick={()=>{ setTab('calendar'); load(); }}>Calendario</button>
      </div>

      {tab === 'requests' && <ManagerView reloadSignal={reloadSignal} />}

      {tab === 'recurring' && (
        <div>
          <h2>Smart Working Ricorsivo</h2>
          <table className="requests">
            <thead>
              <tr><th>Dipendente</th><th>Giorno</th><th>Stato</th><th>Azione</th><th></th></tr>
            </thead>
            <tbody>
              {(recurringRequests||[]).map(x => (
                <tr key={x.id}>
                  <td>{x.employeeName}</td>
                  <td>{x.dayName}</td>
                  <td>{getStatusBadge(x.status)}</td>
                  <td>
                    {x.status === 'Pending' ? (
                      <div className="actions">
                        <button className="btn primary" onClick={() => approveRecurring(x.id)}>Accetta</button>
                        <button className="btn secondary" onClick={() => rejectRecurring(x.id)}>Rifiuta</button>
                      </div>
                    ) : (
                      <div className="actions">
                        <strong>{x.status}</strong>
                      </div>
                    )}
                  </td>
                  <td className="col-delete">
                    <button className="btn danger" onClick={() => removeRecurring(x.id)} aria-label="Elimina richiesta ricorsiva">
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
      )}

      {tab === 'personnel' && (
        <div>
          <h2>Gestione Personale</h2>
          <div style={{display:'flex', gap:20}}>
            <div style={{flex:1}}>
              <h3>Nuovo dipendente</h3>
              <div className="form">
                <input placeholder="Username *" value={newUser.username} onChange={e=>setNewUser({...newUser, username: e.target.value})} />
                <input placeholder="Nome visualizzato" value={newUser.displayName} onChange={e=>setNewUser({...newUser, displayName: e.target.value})} />
                <input placeholder="Email *" value={newUser.email} onChange={e=>setNewUser({...newUser, email: e.target.value})} />
                <select value={newUser.role} onChange={e=>setNewUser({...newUser, role: e.target.value})} className="recurring-select">
                  <option>Employee</option>
                  <option>Admin</option>
                </select>
                <div style={{display:'flex', gap:8}}>
                  <button className="btn primary" onClick={async ()=>{
                    setCreateErr(null); setCreateMsg(null);
                    if (!newUser.username || !newUser.email) { setCreateErr('Username e Email sono obbligatori'); return }
                    try {
                      await createUser(newUser)
                      setCreateMsg('Utente creato. Password temporanea inviata via email.')
                      setNewUser({ username:'', displayName:'', email:'', role:'Employee' })
                      await load()
                    } catch (err) { setCreateErr(err.message || 'Errore creazione utente') }
                  }}>Crea</button>
                  <button className="btn secondary" onClick={()=>{ setNewUser({ username:'', displayName:'', email:'', role:'Employee' }); setCreateErr(null); setCreateMsg(null) }}>Annulla</button>
                </div>
                {createErr && <p className="message error">{createErr}</p>}
                {createMsg && <p className="message success">{createMsg}</p>}
              </div>
            </div>

            <div style={{flex:2}}>
              <h3>Elenco dipendenti</h3>
              <table className="requests">
                <thead><tr><th>Username</th><th>Nome</th><th>Email</th><th>Ruolo</th><th></th></tr></thead>
                <tbody>
                  {(users||[]).map(u => (
                    <tr key={u.username}>
                      <td>{u.username}</td>
                      <td>{u.displayName}</td>
                      <td>{u.email}</td>
                      <td>{u.role}</td>
                      <td className="col-delete">
                        <button className="btn danger" onClick={() => removeUser(u.username)} aria-label="Elimina utente">
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
          </div>
        </div>
      )}

      {tab === 'calendar' && (
        <div>
          <h2>Calendario Smart Working (approvati)</h2>
          <Calendar events={approved} recurringRequests={approvedRecurringRequests} />
        </div>
      )}
    </div>
  )
}
