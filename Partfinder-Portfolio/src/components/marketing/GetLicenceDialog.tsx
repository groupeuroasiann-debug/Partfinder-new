import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Icon } from './SiteChrome'

const fieldClass =
  'w-full h-11 px-3 bg-surface-container-lowest rounded-lg font-body-sm text-body-sm text-on-surface placeholder:text-outline focus:outline-none focus:ring-2 focus:ring-primary border-0'

type GetLicenceDialogProps = {
  open: boolean
  onClose: () => void
}

export function GetLicenceDialog({ open, onClose }: GetLicenceDialogProps) {
  const [organisation, setOrganisation] = useState('')
  const [email, setEmail] = useState('')
  const [mobile, setMobile] = useState('')
  const [error, setError] = useState('')
  const [submitted, setSubmitted] = useState<{
    organisation: string
    email: string
    mobile: string
  } | null>(null)

  useEffect(() => {
    if (!open) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])

  if (!open) return null

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const name = organisation.trim()
    const mail = email.trim()
    const phone = mobile.trim()
    const digits = phone.replace(/\D/g, '')

    if (!name) {
      setError('Enter the organisation name.')
      return
    }
    if (!mail || !mail.includes('@')) {
      setError('Enter a valid email address.')
      return
    }
    if (digits.length < 8) {
      setError('Enter a valid mobile number.')
      return
    }

    setError('')
    setSubmitted({ organisation: name, email: mail, mobile: phone })
  }

  const handleClose = () => {
    setOrganisation('')
    setEmail('')
    setMobile('')
    setError('')
    setSubmitted(null)
    onClose()
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
        aria-labelledby="get-licence-title"
        onClick={(event) => event.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-space-md mb-space-md">
          <div>
            <span className="font-caption text-caption text-primary uppercase font-bold tracking-widest">
              Licensing
            </span>
            <h2 id="get-licence-title" className="font-headline-md text-headline-md text-on-surface font-extrabold tracking-tight m-0 mt-1">
              Get Licence
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

        {submitted ? (
          <div className="flex flex-col gap-space-md">
            <p className="font-body-md text-body-md text-on-surface-variant m-0">
              Licence request received for <strong className="text-on-surface">{submitted.organisation}</strong>.
            </p>
            <div className="bg-surface-container-low rounded-lg p-space-md flex flex-col gap-2">
              <p className="font-body-sm text-body-sm text-on-surface m-0">
                <span className="text-outline">Email</span> · {submitted.email}
              </p>
              <p className="font-body-sm text-body-sm text-on-surface m-0">
                <span className="text-outline">Mobile</span> · {submitted.mobile}
              </p>
            </div>
            <button
              type="button"
              className="bg-primary hover:bg-primary-container text-on-primary font-label-md text-label-md px-space-md py-2.5 rounded-lg transition-colors border-0"
              onClick={handleClose}
            >
              Done
            </button>
          </div>
        ) : (
          <form className="flex flex-col gap-space-md" onSubmit={handleSubmit}>
            <p className="font-body-sm text-body-sm text-on-surface-variant m-0">
              Enter the organisation name, email, and mobile number for this licence.
            </p>
            <div>
              <label className="font-label-sm text-label-sm text-on-surface block mb-1.5" htmlFor="licence-org">
                Organisation name
              </label>
              <input
                id="licence-org"
                className={fieldClass}
                type="text"
                autoComplete="organization"
                placeholder="Northwind Marine"
                value={organisation}
                onChange={(event) => setOrganisation(event.target.value)}
                autoFocus
              />
            </div>
            <div>
              <label className="font-label-sm text-label-sm text-on-surface block mb-1.5" htmlFor="licence-email">
                Email
              </label>
              <input
                id="licence-email"
                className={fieldClass}
                type="email"
                autoComplete="email"
                placeholder="ops@company.com"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
              />
            </div>
            <div>
              <label className="font-label-sm text-label-sm text-on-surface block mb-1.5" htmlFor="licence-mobile">
                Mobile number
              </label>
              <input
                id="licence-mobile"
                className={fieldClass}
                type="tel"
                autoComplete="tel"
                inputMode="tel"
                placeholder="+91 98765 43210"
                value={mobile}
                onChange={(event) => setMobile(event.target.value)}
              />
            </div>
            {error ? (
              <div className="flex items-start gap-2 rounded-lg bg-error-container text-on-error-container px-3 py-2 font-body-sm text-body-sm">
                <Icon name="error" className="text-[18px]" />
                <span>{error}</span>
              </div>
            ) : null}
            <button
              type="submit"
              className="bg-primary hover:bg-primary-container text-on-primary font-label-md text-label-md px-space-md py-2.5 rounded-lg transition-colors flex items-center justify-center gap-1.5 shadow-sm border-0"
            >
              <span>Get Licence</span>
              <Icon name="arrow_forward" className="text-[18px]" />
            </button>
          </form>
        )}
      </div>
    </div>
  )
}
