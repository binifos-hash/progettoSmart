import React, { useState } from 'react'
import { login, forgotPassword, changePassword } from '../api'

export default function Login({ onLogin }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [err, setErr] = useState(null)

  // modals
  const [showForgot, setShowForgot] = useState(false)
  const [forgotEmail, setForgotEmail] = useState('')
  const [forgotMsg, setForgotMsg] = useState(null)

  const [showChange, setShowChange] = useState(false)
  const [changeOld, setChangeOld] = useState('')
  const [changeNew, setChangeNew] = useState('')
  const [changeErr, setChangeErr] = useState(null)

  async function submit(e) {
    e.preventDefault()
    setErr(null)
    try {
      const res = await login(username, password)
      localStorage.setItem('sw_token', res.token)
      localStorage.setItem('sw_user', res.username)
      localStorage.setItem('sw_role', res.role)
      if (res.forcePasswordChange) {
        // show change password modal; do not complete login until changed
        setShowChange(true)
        return
      }
      onLogin(res)
    } catch (e) {
      setErr('Credenziali non valide')
    }
  }

  async function submitForgot(e) {
    e.preventDefault()
    setForgotMsg(null)
    try {
      await forgotPassword(forgotEmail)
      setForgotMsg('Se l\'email è registrata riceverai una password temporanea via email')
    } catch (err) {
      setForgotMsg('Errore durante l\'invio della password temporanea')
    }
  }

  async function submitChange(e) {
    e.preventDefault()
    setChangeErr(null)
    try {
      const oldP = changeOld || null
      await changePassword(oldP, changeNew)
      // login with new password to obtain fresh token and user info
      const u = await login(username, changeNew)
      localStorage.setItem('sw_token', u.token)
      localStorage.setItem('sw_user', u.username)
      localStorage.setItem('sw_role', u.role)
      setShowChange(false)
      onLogin(u)
    } catch (err) {
      setChangeErr('Errore cambiando la password')
    }
  }

  return (
    <>
      <div className="login-card">
        <h2>Accedi</h2>
        <form onSubmit={submit} className="form">
          <input placeholder="Username" value={username} onChange={e=>setUsername(e.target.value)} />
          <input placeholder="Password" type="password" value={password} onChange={e=>setPassword(e.target.value)} />
          <button type="submit">Login</button>
        </form>
        {err && <p className="message error">{err}</p>}
        <div style={{marginTop:8, textAlign:'center'}}>
          <button className="btn ghost" onClick={()=>setShowForgot(true)}>Password dimenticata?</button>
        </div>
      </div>

      {showForgot && (
        <div className="modal-overlay">
          <div className="modal login-card">
            <h3 style={{textAlign:'left', marginTop:0, marginBottom:16, color:'white'}}>Recupero password</h3>
            <form onSubmit={submitForgot} className="form">
              <input placeholder="Email" value={forgotEmail} onChange={e=>setForgotEmail(e.target.value)} />
              <div style={{display:'flex', gap:8}}>
                <button className="btn ghost" type="submit">Invia</button>
                <button className="btn secondary" type="button" onClick={()=>{ setShowForgot(false); setForgotMsg(null); setForgotEmail('') }}>Annulla</button>
              </div>
            </form>
            {forgotMsg && <p className="message success" style={{marginTop:8}}>{forgotMsg}</p>}
          </div>
        </div>
      )}

      {showChange && (
        <div className="modal-overlay">
          <div className="modal login-card">
            <h3 style={{textAlign:'left', marginTop:0, marginBottom:16, color:'white'}}>Cambia password</h3>
            <form onSubmit={submitChange} className="form">
              <input placeholder="Password attuale (se richiesta)" type="password" value={changeOld} onChange={e=>setChangeOld(e.target.value)} />
              <input placeholder="Nuova password" type="password" value={changeNew} onChange={e=>setChangeNew(e.target.value)} />
              <div style={{display:'flex', gap:8}}>
                  <button className="btn primary" type="submit">Cambia</button>
                  <button className="btn secondary" type="button" onClick={()=>setShowChange(false)}>Annulla</button>
                </div>
              </form>
            {changeErr && <p className="message error" style={{marginTop:8}}>{changeErr}</p>}
          </div>
        </div>
      )}
    </>
  )
}
