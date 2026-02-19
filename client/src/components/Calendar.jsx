import React, { useState } from 'react'

function getDaysInMonth(year, month) {
  const date = new Date(year, month, 1)
  const days = []
  while (date.getMonth() === month) {
    days.push(new Date(date))
    date.setDate(date.getDate() + 1)
  }
  return days
}

export default function Calendar({ onSelect, events = [], recurringDays = [], recurringRequests = [] }) {
  const today = new Date()
  const [year, setYear] = useState(today.getFullYear())
  const [month, setMonth] = useState(today.getMonth())
  const days = getDaysInMonth(year, month)
  const [selected, setSelected] = useState(null)

  // events: array of { date: 'YYYY-MM-DD', title?: '...' } or strings
  const eventsMap = events.reduce((acc, ev) => {
    const d = typeof ev === 'string' ? ev : ev.date
    if (!d) return acc
    acc[d] = acc[d] || []
    acc[d].push(typeof ev === 'string' ? '' : (ev.title || ''))
    return acc
  }, {})

  // Merge recurringRequests (array of { dayOfWeek, title }) into eventsMap for the visible month
  try {
    ;(Array.isArray(days) ? days : []).forEach(d => {
      const pad = n => n.toString().padStart(2,'0')
      const iso = `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())}`
      const dow = d.getDay()
      ;(Array.isArray(recurringRequests) ? recurringRequests : []).forEach(rr => {
        if (!rr) return
        const rrDow = rr.dayOfWeek ?? rr.DayOfWeek
        let title = rr.title ?? rr.employeeName ?? rr.EmployeeName
        if (rrDow === dow) {
          eventsMap[iso] = eventsMap[iso] || []
          try { eventsMap[iso].push(title == null ? '' : String(title)) } catch { eventsMap[iso].push('') }
        }
      })
    })
  } catch (e) {
    console.error('Error merging recurringRequests into eventsMap', e)
  }

  function choose(d) {
    const pad = n => n.toString().padStart(2,'0')
    const iso = `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())}`
    setSelected(iso)
    onSelect && onSelect(iso)
  }

  return (
    <div className="calendar">
      <div className="cal-head">
        <button onClick={()=>{ if (month===0){ setMonth(11); setYear(y=>y-1)} else setMonth(m=>m-1)}}>&lt;</button>
        <strong>{year} - {month+1}</strong>
        <button onClick={()=>{ if (month===11){ setMonth(0); setYear(y=>y+1)} else setMonth(m=>m+1)}}>&gt;</button>
      </div>
      <div className="cal-grid">
        {['Lu','Ma','Me','Gi','Ve','Sa','Do'].map(h => <div key={h} className="cal-cell head">{h}</div>)}
        {Array.from({length: new Date(year, month, 1).getDay() === 0 ? 6 : new Date(year, month, 1).getDay()-1}).map((_,i)=>(<div key={`pad-${i}`} className="cal-cell empty"></div>))}
        {days.map(d => {
          const pad = n => n.toString().padStart(2,'0')
          const iso = `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())}`
          const has = eventsMap[iso]
          const dayOfWeek = d.getDay() // 0=Sunday, 1=Monday, etc
          // If recurringRequests provide titles for this dow, they will be merged into eventsMap above.
          // Show the lightweight recurring indicator only when caller passed recurringDays (employee view)
          const recurringIndicator = (recurringDays || []).includes(dayOfWeek) && !(recurringRequests || []).some(rr => rr && (rr.dayOfWeek ?? rr.DayOfWeek) === dayOfWeek && (rr.title ?? rr.employeeName ?? rr.EmployeeName))
          return (
            <div key={iso} className={`cal-cell ${selected===iso? 'selected':''} ${has? 'has-event':''} ${recurringIndicator? 'recurring-day':''}`} onClick={()=>choose(d)}>
              <div>{d.getDate()}</div>
              {recurringIndicator && <div className="recurring-indicator" title="Smart Working ricorsivo">♻</div>}
              {has && <div className="cal-event-dot" />}
              {has && eventsMap[iso].map((t,i)=> t ? <div key={i} className="cal-event-title">{t}</div> : null)}
            </div>
          )
        })}
      </div>
    </div>
  )
}
