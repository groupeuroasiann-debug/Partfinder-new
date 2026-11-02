import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Icon } from './SiteChrome'

const ACCOUNT_SESSION_KEY = 'pf_account_session'

const fieldClass =
  'w-full h-11 px-3 bg-surface-container-lowest rounded-lg font-body-sm text-body-sm text-on-surface placeholder:text-outline focus:outline-none focus:ring-2 focus:ring-primary border-0'

export type AccountSession = {
  email: string
  role: string
  orgCode: string
  accessToken: string
}

function apiBase(): string {
  const raw = import.meta.env.VITE_API_URL
  if (typeof raw === 'string' && raw.trim() !== '') {
    let trimmed = raw.trim().replace(/\/$/, '')
    if (!/^https?:\/\//i.test(trimmed)) trimmed = `https://${trimmed}`
    return trimmed
  }
  return import.meta.env.DEV ? '' : 'https://backend.partfinder.store'
}

function readMessage(body: unknown, fallback: string): string {
  if (!body || typeof body !== 'object') return fallback
  const message = (body as { message?: string | string[] }).message
  if (typeof message === 'string' && message.trim()) return message
  if (Array.isArray(message) && message.length > 0) return String(message[0])
  return fallback
}

export function readAccountSession(): AccountSession | null {
  try {
    const raw = window.localStorage.getItem(ACCOUNT_SESSION_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as AccountSession
    if (!parsed?.email || !parsed?.accessToken) return null
    return parsed
  } catch {
    return null
  }
}

export function clearAccountSession() {
  window.localStorage.removeItem(ACCOUNT_SESSION_KEY)
}

type AccountSignInDialogProps = {
  open: boolean
  onClose: () => void
  onSignedIn: (session: AccountSession) => void
}

export function AccountSignInDialog({ open, onClose, onSignedIn }: AccountSignInDialogProps) {
  const [orgCode, setOrgCode] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [pending, setPending] = useState(false)
  const [mustChange, setMustChange] = useState<AccountSession | null>(null)
  const [nextPassword, setNextPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')

  useEffect(() => {
    if (!open) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])

  if (!open) return null

  const reset = () => {
    setOrgCode('')
    setEmail('')
    setPassword('')
    setError('')
    setPending(false)
    setMustChange(null)
    setNextPassword('')
    setConfirmPassword('')
  }

  const handleClose = () => {
    reset()
    onClose()
  }

  const finish = (session: AccountSession) => {
    window.localStorage.setItem(ACCOUNT_SESSION_KEY, JSON.stringify(session))
    onSignedIn(session)
    handleClose()
  }

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const code = orgCode.trim()
    const mail = email.trim().toLowerCase()
    if (!/^\d{6}$/.test(code)) {
      setError('Organisation code must be 6 digits.')
      return
    }
    if (!mail.includes('@')) {
      setError('Enter the email for this PartFinder account.')
      return
    }
    if (!password) {
      setError('Enter the password.')
      return
    }

    setError('')
    setPending(true)
    try {
      const response = await fetch(`${apiBase()}/api/public/setup/invite-login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          orgCode: code,
          email: mail,
          temporaryPassword: password,
        }),
      })
      const data: unknown = await response.json().catch(() => null)
      const result = data as {
        ok?: boolean
        message?: string
        email?: string
        role?: string
        accessToken?: string
        mustChangePassword?: boolean
      } | null
      if (!response.ok || !result?.ok || !result.accessToken) {
        setError(readMessage(data, result?.message || 'Sign in failed.'))
        return
      }
      const session: AccountSession = {
        email: result.email || mail,
        role: result.role || 'Admin',
        orgCode: code,
        accessToken: result.accessToken,
      }
      if (result.mustChangePassword) {
        setMustChange(session)
        return
      }
      finish(session)
    } catch {
      setError('Cannot reach the server. Is the API running?')
    } finally {
      setPending(false)
    }
  }

  const handleChangePassword = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!mustChange) return
    if (nextPassword.length < 8) {
      setError('New password must be at least 8 characters.')
      return
    }
    if (nextPassword !== confirmPassword) {
      setError('Passwords do not match.')
      return
    }
    setError('')
    setPending(true)
    try {
      const response = await fetch(`${apiBase()}/api/public/setup/change-password`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          orgCode: mustChange.orgCode,
          email: mustChange.email,
          currentPassword: password,
          newPassword: nextPassword,
        }),
      })
      const data: unknown = await response.json().catch(() => null)
      if (!response.ok) {
        setError(readMessage(data, 'Could not update the password.'))
        return
      }
      finish(mustChange)
    } catch {
      setError('Cannot reach the server. Is the API running?')
    } finally {
      setPending(false)
    }
  }

  return (
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center overflow-y-auto bg-inverse-surface/50 px-4 py-8"
      role="presentation"
      onClick={handleClose}
    >
      <div
        className="w-full max-w-lg bg-surface-container-lowest rounded-xl shadow-lg p-space-lg"
        role="dialog"
        aria-modal="true"
        aria-labelledby="account-signin-title"
        onClick={(event) => event.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-space-md mb-space-md">
          <div>
            <span className="font-caption text-caption text-primary uppercase font-bold tracking-widest">
              PartFinder account
            </span>
            <h2 id="account-signin-title" className="font-headline-md text-headline-md text-on-surface font-extrabold tracking-tight m-0 mt-1">
              {mustChange ? 'Set your password' : 'Sign in'}
            </h2>
          </div>
          <button
            type="button"
            className="w-9 h-9 rounded-lg bg-surface-container text-on-surface border-0 flex items-center justify-center hover:bg-surface-container-high"
            onClick={handleClose}
            aria-label="Close"
          >
            <Icon name="close" className="text-[20px]" />
          </button>
        </div>

        {mustChange ? (
          <form className="flex flex-col gap-space-md" onSubmit={handleChangePassword}>
            <p className="font-body-sm text-body-sm text-on-surface-variant m-0">
              This is the same account you use in the PartFinder app. Choose a password before continuing on the web.
            </p>
            <div>
              <label className="font-label-sm text-label-sm text-on-surface block mb-1.5" htmlFor="account-new-password">
                New password
              </label>
              <input
                id="account-new-password"
                className={fieldClass}
                type="password"
                autoComplete="new-password"
                value={nextPassword}
                onChange={(event) => setNextPassword(event.target.value)}
                autoFocus
              />
            </div>
            <div>
              <label className="font-label-sm text-label-sm text-on-surface block mb-1.5" htmlFor="account-confirm-password">
                Confirm password
              </label>
              <input
                id="account-confirm-password"
                className={fieldClass}
                type="password"
                autoComplete="new-password"
                value={confirmPassword}
                onChange={(event) => setConfirmPassword(event.target.value)}
              />
            </div>
            {error ? <ErrorNote message={error} /> : null}
            <button
              type="submit"
              className="bg-primary hover:bg-primary-container text-on-primary font-label-md text-label-md px-space-md py-2.5 rounded-lg transition-colors border-0 disabled:opacity-60"
              disabled={pending}
            >
              {pending ? 'Saving…' : 'Save and continue'}
            </button>
          </form>
        ) : (
          <form className="flex flex-col gap-space-md" onSubmit={handleSubmit}>
            <p className="font-body-sm text-body-sm text-on-surface-variant m-0">
              Sign in with the organisation account you use in the PartFinder Windows app.
            </p>
            <div>
              <label className="font-label-sm text-label-sm text-on-surface block mb-1.5" htmlFor="account-org-code">
                Organisation code
              </label>
              <input
                id="account-org-code"
                className={fieldClass}
                inputMode="numeric"
                autoComplete="off"
                maxLength={6}
                placeholder="6-digit code"
                value={orgCode}
                onChange={(event) => setOrgCode(event.target.value.replace(/\D/g, '').slice(0, 6))}
                autoFocus
              />
            </div>
            <div>
              <label className="font-label-sm text-label-sm text-on-surface block mb-1.5" htmlFor="account-email">
                Email
              </label>
              <input
                id="account-email"
                className={fieldClass}
                type="email"
                autoComplete="username"
                placeholder="admin@company.com"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
              />
            </div>
            <div>
              <label className="font-label-sm text-label-sm text-on-surface block mb-1.5" htmlFor="account-password">
                Password
              </label>
              <input
                id="account-password"
                className={fieldClass}
                type="password"
                autoComplete="current-password"
                placeholder="Account password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
            </div>
            {error ? <ErrorNote message={error} /> : null}
            <button
              type="submit"
              className="bg-primary hover:bg-primary-container text-on-primary font-label-md text-label-md px-space-md py-2.5 rounded-lg transition-colors flex items-center justify-center gap-1.5 shadow-sm border-0 disabled:opacity-60"
              disabled={pending}
            >
              <span>{pending ? 'Signing in…' : 'Sign in'}</span>
              <Icon name="arrow_forward" className="text-[18px]" />
            </button>
          </form>
        )}
      </div>
    </div>
  )
}

function ErrorNote({ message }: { message: string }) {
  return (
    <div className="flex items-start gap-2 rounded-lg bg-error-container text-on-error-container px-3 py-2 font-body-sm text-body-sm">
      <Icon name="error" className="text-[18px]" />
      <span>{message}</span>
    </div>
  )
}
