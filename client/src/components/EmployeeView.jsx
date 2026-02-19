import React, { useEffect, useState } from 'react'
import { createRequest, me, fetchMyRequests, createRecurringRequest, fetchMyRecurringRequests, deleteRequest, deleteRecurringRequest } from '../api'
import Calendar from './Calendar'

export default function EmployeeView() {
  const [selected, setSelected] = useState('')
  const [message, setMessage] = useState(null)
  const [user, setUser] = useState(null)
  const [myRequests, setMyRequests] = useState([])
  const [myRecurringRequests, setMyRecurringRequests] = useState([])
  const [recurringDay, setRecurringDay] = useState(0) // 0=Monday (first in new order)
  
  // Days display order: Monday first, Sunday last
  const weekDaysDisplay = ['Lunedì', 'Martedì', 'Mercoledì', 'Giovedì', 'Venerdì', 'Sabato', 'Domenica']
  // Mapping to backend dayOfWeek (0=Sunday, 1=Monday, etc)
  const weekDaysMapping = [1, 2, 3, 4, 5, 6, 0]

  useEffect(()=>{
    (async()=>{
      const u = await me()
      setUser(u)
      await loadMyRequests()
      await loadMyRecurringRequests()
    })()
  },[])

  async function loadMyRequests(){
    try{
      const r = await fetchMyRequests()
      setMyRequests(r)
    }catch(e){ }
  }

  async function loadMyRecurringRequests(){
    try{
      const r = await fetchMyRecurringRequests()
      setMyRecurringRequests(r)
    }catch(e){ }
  }

  async function submitRecurring(e){
    e.preventDefault()
    try{
      const dayOfWeek = weekDaysMapping[recurringDay]
      await createRecurringRequest(dayOfWeek, weekDaysDisplay[recurringDay])
      setMessage('Richiesta ricorsiva inviata')
      await loadMyRecurringRequests()
    }catch(err){
      setMessage('Errore inviando la richiesta ricorsiva')
    }
  }

  async function removeRecurring(id){
    if (!confirm('Confermi di eliminare la richiesta ricorsiva?')) return
    try{
      await deleteRecurringRequest(id)
    }catch(e){ console.error(e) }
    await loadMyRecurringRequests()
  }

  async function submit(e){
    e.preventDefault()
    if (!selected) return setMessage('Seleziona una data')
    try{
      await createRequest({ date: selected })
      setMessage('Richiesta inviata')
      setSelected('')
      await loadMyRequests()
    }catch(err){
      setMessage('Errore inviando la richiesta')
    }
  }

  async function removeRequest(id){
    if (!confirm('Confermi di eliminare la richiesta?')) return
    try{
      await deleteRequest(id)
    }catch(e){ console.error(e) }
    await loadMyRequests()
  }

  function getStatusBadge(status) {
    const classes = `badge ${status.toLowerCase()}`
    const labels = { Approved: '✓ Approved', Rejected: '✕ Rejected', Pending: '⊙ Pending' }
    return <span className={classes}>{labels[status] || status}</span>
  }

  // Get approved recurring days for calendar display
  const approvedRecurringDays = (myRecurringRequests || [])
    .filter(r => r.status === 'Approved')
    .map(r => r.dayOfWeek)

  return (
    <div className="card">
      <h2>Invia richiesta per data specifica</h2>
      <Calendar onSelect={d=>setSelected(d)} recurringDays={approvedRecurringDays} />
      <div style={{marginTop:20}}>
        <button className="btn primary" onClick={submit} disabled={!selected}>Invia richiesta per {selected || '---'}</button>
      </div>
      {message && <p className={`message ${message.includes('Errore') ? 'error' : 'success'}`}>{message}</p>}

      <h3>Richiesta Smart Working Ricorsivo</h3>
      <div style={{marginBottom: 16}}>
        <label style={{display: 'block', marginBottom: 8}}>Scegli il giorno della settimana:</label>
        <select value={recurringDay} onChange={e => setRecurringDay(parseInt(e.target.value))} className="recurring-select">
          {weekDaysDisplay.map((day, i) => <option key={i} value={i}>{day}</option>)}
        </select>
        <div style={{marginTop: 12}}>
          <button className="btn primary" onClick={submitRecurring}>Richiedi {weekDaysDisplay[recurringDay]} fisso</button>
        </div>
      </div>

      <h3>Le tue richieste singole</h3>
      <table className="requests">
        <thead><tr><th>Data</th><th>Stato</th><th></th></tr></thead>
        <tbody>
          {(myRequests||[]).map(x => (
            <tr key={x.id}>
              <td>{new Date(x.date).toLocaleDateString()}</td>
              <td>{getStatusBadge(x.status ?? x.Status)}</td>
              <td className="col-delete">
                <button className="btn danger" onClick={() => removeRequest(x.id)} aria-label="Elimina richiesta">
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

      <h3>Le tue richieste ricorsive</h3>
      <table className="requests">
        <thead><tr><th>Giorno</th><th>Stato</th><th></th></tr></thead>
        <tbody>
          {(myRecurringRequests||[]).map(x => (
            <tr key={x.id}>
              <td>{x.dayName}</td>
              <td>{getStatusBadge(x.status)}</td>
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
  )
}
