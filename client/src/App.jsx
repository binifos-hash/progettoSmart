import React, { useEffect, useState } from 'react'
import EmployeeView from './components/EmployeeView'
import ManagerView from './components/ManagerView'
import AdminView from './components/AdminView'
import Login from './components/Login'
import { me, updateTheme } from './api'

export default function App() {
  const [tab, setTab] = useState('employee')
  const [user, setUser] = useState(null)

  useEffect(()=>{
    (async()=>{
      const u = await me()
      setUser(u)
      if (u) setTab(u.role === 'Admin' ? 'manager' : 'employee')
    })()
  },[])

  useEffect(() => {
    if (user?.theme === 'dark') document.body.classList.add('dark')
    else document.body.classList.remove('dark')
    return () => document.body.classList.remove('dark')
  }, [user?.theme])

  useEffect(() => {
    if (!user) document.body.classList.add('login-dark')
    else document.body.classList.remove('login-dark')
    return () => document.body.classList.remove('login-dark')
  }, [user])

  function handleLogin(u){
    setUser(u)
    setTab(u.role === 'Admin' ? 'manager' : 'employee')
  }

  return (
    <div className={`container ${user?.theme === 'dark' ? 'dark' : ''}`}>
      {!user ? (
        <Login onLogin={handleLogin} />
      ) : (
        <>
          <div className={`header-top ${user?.theme === 'dark' ? 'dark' : ''}`}>
            <h1>SmartWork Requests</h1>
            <div className="header-right">
              <span className="user-info">👤 {user.username}</span>
              <button className="theme-toggle" onClick={async ()=>{
                const next = (user.theme === 'dark') ? 'light' : 'dark'
                try { await updateTheme(next); setUser(u => ({ ...u, theme: next })) } catch { setUser(u => ({ ...u, theme: next })) }
              }}>{user?.theme === 'dark' ? '🌙' : '☀️'}</button>
              <button className="logout-btn" onClick={() => { localStorage.removeItem('sw_token'); localStorage.removeItem('sw_user'); localStorage.removeItem('sw_role'); setUser(null); }}>Logout</button>
            </div>
          </div>

          {/* top role tabs removed as requested */}

          {user.role === 'Employee' && tab === 'employee' && <EmployeeView />}
          {user.role === 'Admin' && tab === 'manager' && <AdminView />}
        </>
      )}
    </div>
  )
}
