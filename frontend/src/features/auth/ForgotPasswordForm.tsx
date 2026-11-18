import React, { useState } from 'react';
import { Mail, KeyRound, AlertCircle, Loader2, CheckCircle2, ArrowLeft, Inbox } from 'lucide-react';
import { forgotPassword } from './api';

interface ForgotPasswordFormProps {
  onNavigateToLogin: () => void;
  initialEmail?: string;
  onNavigateToMailbox?: () => void;
}

export const ForgotPasswordForm: React.FC<ForgotPasswordFormProps> = ({
  onNavigateToLogin,
  initialEmail = '',
  onNavigateToMailbox,
}) => {
  const [email, setEmail] = useState<string>(initialEmail);
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [submitted, setSubmitted] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setErrorMessage(null);

    const trimmedEmail = email.trim();
    if (!trimmedEmail) {
      setErrorMessage('Please enter your university email address.');
      return;
    }

    if (!trimmedEmail.includes('@')) {
      setErrorMessage('Please enter a valid university email address.');
      return;
    }

    setIsSubmitting(true);
    try {
      await forgotPassword({ email: trimmedEmail });
      setSubmitted(true);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Unable to process password reset request.';
      setErrorMessage(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  if (submitted) {
    return (
      <div className="w-full max-w-md mx-auto bg-white border border-slate-200 rounded-2xl shadow-sm p-6 sm:p-8 space-y-6">
        <div className="text-center space-y-2">
          <div className="w-12 h-12 bg-emerald-100 text-emerald-700 rounded-xl flex items-center justify-center mx-auto shadow-xs">
            <CheckCircle2 className="w-6 h-6 text-emerald-600" />
          </div>
          <h2 className="text-xl font-bold text-slate-900 tracking-tight">Check your email</h2>
          <p className="text-xs text-slate-600 leading-relaxed">
            If an eligible account is registered under <strong className="text-slate-800">{email.trim()}</strong>,
            a password reset link has been dispatched.
          </p>
        </div>

        {onNavigateToMailbox && (
          <div className="p-4 bg-sky-50 border border-sky-100 rounded-xl space-y-2">
            <div className="flex items-center space-x-2 text-sky-800 font-semibold text-xs">
              <Inbox className="w-4 h-4 text-sky-600" />
              <span>Development Environment Mailbox</span>
            </div>
            <p className="text-xs text-sky-700 leading-relaxed">
              In local development, outgoing transactional emails are captured in the built-in development mailbox without external network traffic.
            </p>
            <button
              type="button"
              data-testid="forgot-password-mailbox-btn"
              onClick={onNavigateToMailbox}
              className="mt-1 inline-flex items-center text-xs font-bold text-sky-700 hover:text-sky-900 underline cursor-pointer"
            >
              Open Development Mailbox to view reset email &rarr;
            </button>
          </div>
        )}

        <div className="pt-2">
          <button
            type="button"
            data-testid="forgot-password-return-login-btn"
            onClick={onNavigateToLogin}
            className="w-full inline-flex items-center justify-center space-x-2 px-4 py-2.5 text-sm font-semibold text-slate-700 bg-slate-100 hover:bg-slate-200 rounded-lg transition cursor-pointer"
          >
            <ArrowLeft className="w-4 h-4" />
            <span>Return to Sign In</span>
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="w-full max-w-md mx-auto bg-white border border-slate-200 rounded-2xl shadow-sm p-6 sm:p-8">
      <div className="text-center space-y-2 mb-6">
        <div className="w-12 h-12 bg-sky-100 text-sky-700 rounded-xl flex items-center justify-center mx-auto shadow-xs">
          <KeyRound className="w-6 h-6" />
        </div>
        <h2 className="text-xl font-bold text-slate-900 tracking-tight">Forgot your password?</h2>
        <p className="text-xs text-slate-500">
          Enter your university email address and we'll send you a password reset link.
        </p>
      </div>

      {errorMessage && (
        <div
          role="alert"
          aria-live="polite"
          data-testid="forgot-password-error-banner"
          className="mb-5 rounded-xl p-4 flex items-start space-x-3 bg-rose-50 border border-rose-200 text-rose-900 animate-in fade-in"
        >
          <AlertCircle className="w-5 h-5 text-rose-600 flex-shrink-0 mt-0.5" />
          <div className="text-xs leading-relaxed flex-1">
            <span className="font-semibold block">Password Recovery Request Failed</span>
            <span>{errorMessage}</span>
          </div>
        </div>
      )}

      <form onSubmit={handleSubmit} noValidate className="space-y-4">
        <div>
          <label htmlFor="forgot-email" className="block text-xs font-semibold text-slate-700 uppercase tracking-wider mb-1.5">
            University Email Address <span className="text-rose-500">*</span>
          </label>
          <div className="relative">
            <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-slate-400">
              <Mail className="w-4 h-4" />
            </div>
            <input
              id="forgot-email"
              type="email"
              autoComplete="email"
              required
              value={email}
              onChange={(e) => {
                setEmail(e.target.value);
                if (errorMessage) setErrorMessage(null);
              }}
              placeholder="user@campus.internal"
              disabled={isSubmitting}
              className="w-full pl-9 pr-3 py-2 text-sm bg-slate-50 border border-slate-300 rounded-lg text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-sky-500 focus:bg-white transition disabled:opacity-50"
            />
          </div>
        </div>

        <button
          type="submit"
          data-testid="forgot-password-submit-btn"
          disabled={isSubmitting || !email.trim()}
          aria-busy={isSubmitting}
          className="w-full mt-2 inline-flex items-center justify-center space-x-2 px-4 py-2.5 text-sm font-semibold text-white bg-sky-600 hover:bg-sky-700 active:bg-sky-800 disabled:opacity-60 rounded-lg shadow-sm transition cursor-pointer"
        >
          {isSubmitting ? (
            <>
              <Loader2 className="w-4 h-4 animate-spin" />
              <span>Sending reset link...</span>
            </>
          ) : (
            <span>Send Reset Link</span>
          )}
        </button>
      </form>

      <div className="mt-6 pt-4 border-t border-slate-100 text-center">
        <button
          type="button"
          onClick={onNavigateToLogin}
          className="inline-flex items-center space-x-1.5 text-xs font-semibold text-slate-600 hover:text-slate-900 transition focus:outline-none cursor-pointer"
        >
          <ArrowLeft className="w-3.5 h-3.5" />
          <span>Back to Sign In</span>
        </button>
      </div>
    </div>
  );
};
