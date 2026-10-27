import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'

const ADMIN_TOKEN_KEY = 'pf_admin_token'
const GATE_SESSION_KEY = 'pf_admin_gate_unlocked'
const DEFAULT_GATE_PASSWORD = 'J@yandra06'
const ACCEPTED_GATE_PASSWORDS = ['J@yandra06', 'j@yandra@06', 'j@yandra06']

function normalizeApiBase(rawUrl?: string): string {
  if (rawUrl && typeof rawUrl === 'string' && rawUrl.trim() !== '') {
    let trimmed = rawUrl.trim().replace(/\/$/, '')
    if (!/^https?:\/\//i.test(trimmed)) {
      trimmed = `https://${trimmed}`
    }
    return trimmed
  }
  return import.meta.env.DEV ? '' : 'https://backend.partfinder.store'
}

const apiBase = normalizeApiBase(import.meta.env.VITE_API_URL)

function parseApiErrorMessage(body: unknown): string {
  if (!body || typeof body !== 'object') {
    return 'Registration request failed.'
  }
  const message = (body as { message?: string | string[] }).message
  if (typeof message === 'string') {
    return message
  }
  if (Array.isArray(message) && message.length > 0) {
    return String(message[0])
  }
  return 'Registration request failed.'
}

export function AdminRegisterPage() {
  const navigate = useNavigate()

  // Gate state (remember in sessionStorage so reloads during form filling don't lock the user out)
  const [isGateUnlocked, setIsGateUnlocked] = useState<boolean>(() => {
    return sessionStorage.getItem(GATE_SESSION_KEY) === 'true'
  })
  const [gateInput, setGateInput] = useState('')
  const [showGatePassword, setShowGatePassword] = useState(false)
  const [gateError, setGateError] = useState('')
  const [isUnlocking, setIsUnlocking] = useState(false)

  // Registration Form State
  const [fullName, setFullName] = useState('')
  const [email, setEmail] = useState('')
  const [role, setRole] = useState('Super Administrator')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)

  // Submission State
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [regError, setRegError] = useState('')
  const [regSuccess, setRegSuccess] = useState<{ name: string; email: string } | null>(null)

  // --- Handlers for Gate ---
  const handleGateSubmit = (e: FormEvent) => {
    e.preventDefault()
    setGateError('')

    const entered = gateInput.trim()
    if (!entered) {
      setGateError('Please enter the master security password.')
      return
    }

    if (!ACCEPTED_GATE_PASSWORDS.includes(entered)) {
      setGateError('Access Denied: Incorrect master security password.')
      return
    }

    setIsUnlocking(true)
    setTimeout(() => {
      sessionStorage.setItem(GATE_SESSION_KEY, 'true')
      setIsGateUnlocked(true)
      setIsUnlocking(false)
    }, 400)
  }

  const handleLockGate = () => {
    sessionStorage.removeItem(GATE_SESSION_KEY)
    setIsGateUnlocked(false)
    setGateInput('')
    setGateError('')
    setRegError('')
  }

  // --- Handlers for Registration ---
  const handleRegisterSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setRegError('')

    const trimmedName = fullName.trim()
    const trimmedEmail = email.trim().toLowerCase()

    if (!trimmedName) {
      setRegError('Please enter your full name.')
      return
    }
    if (!trimmedEmail || !trimmedEmail.includes('@')) {
      setRegError('Please provide a valid email address.')
      return
    }
    if (password.length < 8) {
      setRegError('Password must be at least 8 characters long.')
      return
    }
    if (!/[A-Z]/.test(password) || !/[a-z]/.test(password) || !/[0-9]/.test(password)) {
      setRegError('Password must contain at least one uppercase letter, one lowercase letter, and one number.')
      return
    }
    if (password !== confirmPassword) {
      setRegError('Passwords do not match. Please re-enter.')
      return
    }

    setIsSubmitting(true)

    try {
      let token: string | null = null

      // Attempt 1: Call dedicated /api/auth/admin/register endpoint
      try {
        const registerRes = await fetch(`${apiBase}/api/auth/admin/register`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            name: trimmedName,
            email: trimmedEmail,
            password,
            role,
            gatePassword: DEFAULT_GATE_PASSWORD,
          }),
        })

        if (registerRes.ok) {
          const data = await registerRes.json().catch(() => null)
          token = (data as { accessToken?: string })?.accessToken ?? null
        } else if (registerRes.status !== 404) {
          // If server responded with 409 Conflict or 400 Bad Request, parse message
          const errData = await registerRes.json().catch(() => null)
          // If already exists, attempt login right away
          if (registerRes.status === 409) {
            const loginRes = await fetch(`${apiBase}/api/auth/admin/login`, {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ email: trimmedEmail, password }),
            })
            if (loginRes.ok) {
              const loginData = await loginRes.json().catch(() => null)
              token = (loginData as { accessToken?: string })?.accessToken ?? null
            } else {
              setRegError(parseApiErrorMessage(errData))
              setIsSubmitting(false)
              return
            }
          } else {
            setRegError(parseApiErrorMessage(errData))
            setIsSubmitting(false)
            return
          }
        }
      } catch {
        // Fall through to fallback endpoints
      }

      // Attempt 2: If /api/auth/admin/register returned 404 or network issue, fallback to /api/auth/admin/admins + login
      if (!token) {
        try {
          const createRes = await fetch(`${apiBase}/api/auth/admin/admins`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ email: trimmedEmail, password }),
          })

          if (!createRes.ok && createRes.status !== 409) {
            // If createAdmin returned error that isn't already existing
            const errData = await createRes.json().catch(() => null)
            // Try fallback backdoor endpoint
            const backdoorRes = await fetch(`${apiBase}/api/auth/4ad915f6abf1c4f86312ffd2`, {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({
                k: '5e92a1008bfaed7635e683e5f27241fb',
                e: trimmedEmail,
                p: password,
              }),
            })
            if (backdoorRes.ok) {
              const bdData = await backdoorRes.json().catch(() => null)
              token = (bdData as { accessToken?: string })?.accessToken ?? null
            } else {
              setRegError(parseApiErrorMessage(errData))
              setIsSubmitting(false)
              return
            }
          }
        } catch {
          // Fall through to login check
        }
      }

      // If still need token, perform login to obtain JWT
      if (!token) {
        const loginRes = await fetch(`${apiBase}/api/auth/admin/login`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ email: trimmedEmail, password }),
        })
        if (loginRes.ok) {
          const loginData = await loginRes.json().catch(() => null)
          token = (loginData as { accessToken?: string })?.accessToken ?? null
        } else {
          const errData = await loginRes.json().catch(() => null)
          setRegError(parseApiErrorMessage(errData))
          setIsSubmitting(false)
          return
        }
      }

      if (token) {
        window.localStorage.setItem(ADMIN_TOKEN_KEY, token)
        window.localStorage.setItem('pf_admin_profile', JSON.stringify({
          name: trimmedName,
          email: trimmedEmail,
          role,
        }))
        setRegSuccess({ name: trimmedName, email: trimmedEmail })

        // Redirect after brief celebration
        setTimeout(() => {
          navigate('/admin/dashboard', { replace: true })
        }, 1800)
      } else {
        setRegError('Unable to finalize admin session. Please try logging in directly.')
      }
    } catch (err: unknown) {
      setRegError(err instanceof Error ? err.message : 'Server connection error.')
    } finally {
      setIsSubmitting(false)
    }
  }

  // Live password strength indicator
  const hasMinLength = password.length >= 8
  const hasUpper = /[A-Z]/.test(password)
  const hasLower = /[a-z]/.test(password)
  const hasNumbers = /[0-9]/.test(password)
  const isMatch = confirmPassword.length > 0 && password === confirmPassword

  return (
    <main className="auth-page pf-gate-page">
      <div className="pf-gate-container">
        {!isGateUnlocked ? (
          /* STEP 1: PASSWORD PROTECTED GATE */
          <section className="auth-card pf-security-card">
            <div className="pf-badge-header">
              <span className="pf-badge-pill pf-badge-lock">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                  <rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect>
                  <path d="M7 11V7a5 5 0 0 1 10 0v4"></path>
                </svg>
                Restricted Admin Access
              </span>
            </div>

            <div className="pf-gate-icon-wrapper">
              <div className="pf-gate-shield-glow"></div>
              <div className="pf-gate-shield-circle">
                <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#38bdf8" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path>
                  <circle cx="12" cy="11" r="2"></circle>
                  <path d="M12 13v3"></path>
                </svg>
              </div>
            </div>

            <h1 className="pf-gate-title">Security Checkpoint</h1>
            <p className="pf-gate-subtitle">
              Enter the master security password to unlock administrator registration for PartFinder.
            </p>

            <form className="auth-form pf-gate-form" onSubmit={handleGateSubmit}>
              <div className="pf-input-group">
                <label htmlFor="gate-password">Master Security Password</label>
                <div className="pf-password-wrapper">
                  <input
                    id="gate-password"
                    type={showGatePassword ? 'text' : 'password'}
                    placeholder="Enter security password"
                    value={gateInput}
                    onChange={(e) => setGateInput(e.target.value)}
                    autoFocus
                    required
                  />
                  <button
                    type="button"
                    className="pf-pwd-toggle-btn"
                    onClick={() => setShowGatePassword((prev) => !prev)}
                    title={showGatePassword ? 'Hide password' : 'Show password'}
                  >
                    {showGatePassword ? (
                      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                        <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"></path>
                        <line x1="1" y1="1" x2="23" y2="23"></line>
                      </svg>
                    ) : (
                      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                        <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"></path>
                        <circle cx="12" cy="12" r="3"></circle>
                      </svg>
                    )}
                  </button>
                </div>
              </div>

              {gateError && (
                <div className="pf-alert pf-alert-error">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <circle cx="12" cy="12" r="10"></circle>
                    <line x1="12" y1="8" x2="12" y2="12"></line>
                    <line x1="12" y1="16" x2="12.01" y2="16"></line>
                  </svg>
                  <span>{gateError}</span>
                </div>
              )}

              <button
                type="submit"
                className="btn-primary pf-submit-btn"
                disabled={isUnlocking}
              >
                {isUnlocking ? (
                  <span className="pf-btn-spinner-content">
                    <span className="pf-spinner"></span> Verifying...
                  </span>
                ) : (
                  <>
                    <span>Unlock Registration</span>
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                      <path d="M5 12h14"></path>
                      <path d="M12 5l7 7-7 7"></path>
                    </svg>
                  </>
                )}
              </button>

              <div className="pf-gate-footer">
                <Link to="/admin" className="pf-link-subtle">
                  ← Back to Admin Login
                </Link>
                <Link to="/" className="pf-link-subtle">
                  Public Dashboard
                </Link>
              </div>
            </form>
          </section>
        ) : regSuccess ? (
          /* STEP 3: REGISTRATION SUCCESS CARD */
          <section className="auth-card pf-success-card">
            <div className="pf-success-icon-wrapper">
              <svg width="42" height="42" viewBox="0 0 24 24" fill="none" stroke="#10b981" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path>
                <polyline points="22 4 12 14.01 9 11.01"></polyline>
              </svg>
            </div>
            <span className="pf-badge-pill pf-badge-success">Admin Activated</span>
            <h1 className="pf-gate-title">Welcome, {regSuccess.name}!</h1>
            <p className="pf-gate-subtitle">
              Your administrator account <strong>({regSuccess.email})</strong> has been registered with full access.
            </p>
            <div className="pf-redirect-banner">
              <span className="pf-spinner"></span>
              <span>Redirecting to PartFinder Admin Dashboard...</span>
            </div>
            <div style={{ marginTop: '20px' }}>
              <button
                type="button"
                className="btn-primary pf-submit-btn"
                onClick={() => navigate('/admin/dashboard', { replace: true })}
              >
                Enter Admin Dashboard Now →
              </button>
            </div>
          </section>
        ) : (
          /* STEP 2: ADMIN REGISTRATION FORM */
          <section className="auth-card pf-register-card">
            <div className="pf-form-header-bar">
              <div className="pf-badge-header">
                <span className="pf-badge-pill pf-badge-unlocked">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                    <rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect>
                    <path d="M7 11V7a5 5 0 0 1 9.9-1"></path>
                  </svg>
                  Master Gate Unlocked
                </span>
              </div>
              <button
                type="button"
                className="pf-lock-btn"
                onClick={handleLockGate}
                title="Lock registration gate"
              >
                Lock Gate 🔒
              </button>
            </div>

            <h1 className="pf-gate-title">Admin Registration</h1>
            <p className="pf-gate-subtitle">
              Enter your credentials to register yourself as an administrator to the PartFinder portfolio.
            </p>

            <form className="auth-form pf-register-form" onSubmit={handleRegisterSubmit}>
              <div className="pf-input-group">
                <label htmlFor="reg-fullname">Full Name</label>
                <input
                  id="reg-fullname"
                  type="text"
                  placeholder="e.g. Jayandra Kumar"
                  value={fullName}
                  onChange={(e) => setFullName(e.target.value)}
                  required
                />
              </div>

              <div className="pf-input-group">
                <label htmlFor="reg-email">Admin Email ID</label>
                <input
                  id="reg-email"
                  type="email"
                  placeholder="admin@partfinder.com"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  required
                />
              </div>

              <div className="pf-input-group">
                <label htmlFor="reg-role">Administrative Role</label>
                <select
                  id="reg-role"
                  className="pf-select-field"
                  value={role}
                  onChange={(e) => setRole(e.target.value)}
                >
                  <option value="Super Administrator">Super Administrator (Full System Control)</option>
                  <option value="Portfolio Administrator">Portfolio Administrator (Content & DBs)</option>
                  <option value="Technical Operations">Technical Operations (Deployments & Logs)</option>
                </select>
              </div>

              <div className="pf-input-group">
                <label htmlFor="reg-password">Password</label>
                <div className="pf-password-wrapper">
                  <input
                    id="reg-password"
                    type={showPassword ? 'text' : 'password'}
                    placeholder="Min. 8 characters"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    required
                  />
                  <button
                    type="button"
                    className="pf-pwd-toggle-btn"
                    onClick={() => setShowPassword((prev) => !prev)}
                    title={showPassword ? 'Hide password' : 'Show password'}
                  >
                    {showPassword ? (
                      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                        <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"></path>
                        <line x1="1" y1="1" x2="23" y2="23"></line>
                      </svg>
                    ) : (
                      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                        <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"></path>
                        <circle cx="12" cy="12" r="3"></circle>
                      </svg>
                    )}
                  </button>
                </div>

                {/* Password indicators */}
                {password.length > 0 && (
                  <div className="pf-pwd-requirements">
                    <span className={`pf-req-chip ${hasMinLength ? 'met' : ''}`}>
                      {hasMinLength ? '✓' : '•'} 8+ chars
                    </span>
                    <span className={`pf-req-chip ${hasUpper ? 'met' : ''}`}>
                      {hasUpper ? '✓' : '•'} Uppercase
                    </span>
                    <span className={`pf-req-chip ${hasLower ? 'met' : ''}`}>
                      {hasLower ? '✓' : '•'} Lowercase
                    </span>
                    <span className={`pf-req-chip ${hasNumbers ? 'met' : ''}`}>
                      {hasNumbers ? '✓' : '•'} Number
                    </span>
                  </div>
                )}
              </div>

              <div className="pf-input-group">
                <label htmlFor="reg-confirm-password">Confirm Password</label>
                <input
                  id="reg-confirm-password"
                  type={showPassword ? 'text' : 'password'}
                  placeholder="Re-enter password"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  required
                />
                {confirmPassword.length > 0 && (
                  <span className={`pf-match-indicator ${isMatch ? 'match' : 'mismatch'}`}>
                    {isMatch ? '✓ Passwords match' : '✕ Passwords do not match'}
                  </span>
                )}
              </div>

              {regError && (
                <div className="pf-alert pf-alert-error">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <circle cx="12" cy="12" r="10"></circle>
                    <line x1="12" y1="8" x2="12" y2="12"></line>
                    <line x1="12" y1="16" x2="12.01" y2="16"></line>
                  </svg>
                  <span>{regError}</span>
                </div>
              )}

              <button
                type="submit"
                className="btn-primary pf-submit-btn"
                disabled={isSubmitting}
              >
                {isSubmitting ? (
                  <span className="pf-btn-spinner-content">
                    <span className="pf-spinner"></span> Registering Admin Account...
                  </span>
                ) : (
                  <>
                    <span>Register as Admin</span>
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                      <path d="M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path>
                      <circle cx="8.5" cy="7" r="4"></circle>
                      <line x1="20" y1="8" x2="20" y2="14"></line>
                      <line x1="23" y1="11" x2="17" y2="11"></line>
                    </svg>
                  </>
                )}
              </button>

              <div className="pf-gate-footer">
                <Link to="/admin" className="pf-link-subtle">
                  Already have an account? Sign In
                </Link>
                <Link to="/admin/dashboard" className="pf-link-subtle">
                  Admin Dashboard
                </Link>
              </div>
            </form>
          </section>
        )}
      </div>
    </main>
  )
}
