import React, { useState } from 'react';
import { LogIn, Lock, Mail, AlertCircle, Eye, EyeOff, Loader2, MailWarning, CheckCircle2 } from 'lucide-react';
import { useAuth } from './useAuth';

interface LoginFormProps {
  onSuccess?: () => void;
  onNavigateToRegister?: () => void;
  onNavigateToForgotPassword?: () => void;
  initialEmail?: string;
  onNavigateToMailbox?: () => void;
  successMessage?: string | null;
}

export const LoginForm: React.FC<LoginFormProps> = ({
  onSuccess,
  onNavigateToRegister,
  onNavigateToForgotPassword,
  initialEmail = '',
  onNavigateToMailbox,
  successMessage,
}) => {
  const { login, isLoading, error, clearError } = useAuth();
  const [email, setEmail] = useState(initialEmail);
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [clientValidation, setClientValidation] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    clearError();
    setClientValidation(null);

    const trimmedEmail = email.trim();
    if (!trimmedEmail) {
      setClientValidation('Please enter your university email address.');
      return;
    }

    if (!password) {
      setClientValidation('Please enter your account password.');
      return;
    }

    const success = await login({
      email: trimmedEmail,
      password,
      rememberMe,
    });

    if (success && onSuccess) {
      onSuccess();
    }
  };

  const displayError = clientValidation || error;
  const isEmailUnconfirmed = displayError?.toLowerCase().includes('confirm your email');

  return (
    <div className="w-full max-w-md mx-auto bg-white border border-slate-200 rounded-2xl shadow-sm p-6 sm:p-8">
      <div className="text-center space-y-2 mb-6">
        <div className="w-12 h-12 bg-sky-100 text-sky-700 rounded-xl flex items-center justify-center mx-auto shadow-xs">
          <LogIn className="w-6 h-6" />
        </div>
        <h2 className="text-xl font-bold text-slate-900 tracking-tight">Sign in to CampusVenue</h2>
        <p className="text-xs text-slate-500">
          Enter your university credentials to access the venue management portal.
        </p>
      </div>

      {successMessage && (
        <div
          role="status"
          aria-live="polite"
          data-testid="login-success-banner"
          className="mb-5 rounded-xl p-4 flex items-start space-x-3 bg-emerald-50 border border-emerald-200 text-emerald-900 animate-in fade-in"
        >
          <CheckCircle2 className="w-5 h-5 text-emerald-600 flex-shrink-0 mt-0.5" />
          <div className="text-xs leading-relaxed flex-1">
            <span className="font-semibold block">
              {successMessage.toLowerCase().includes('change') ? 'Password Changed Successfully' : 'Password Reset Completed'}
            </span>
            <span>{successMessage}</span>
          </div>
        </div>
      )}

      {displayError && (
        <div
          role="alert"
          aria-live="polite"
          data-testid="login-error-banner"
          className={`mb-5 rounded-xl p-4 flex items-start space-x-3 ${
            isEmailUnconfirmed
              ? 'bg-amber-50 border border-amber-200 text-amber-900'
              : 'bg-rose-50 border border-rose-200 text-rose-900'
          }`}
        >
          {isEmailUnconfirmed ? (
            <MailWarning className="w-5 h-5 text-amber-600 flex-shrink-0 mt-0.5" />
          ) : (
            <AlertCircle className="w-5 h-5 text-rose-600 flex-shrink-0 mt-0.5" />
          )}
          <div className="text-xs leading-relaxed flex-1">
            <span className="font-semibold block">
              {isEmailUnconfirmed ? 'Email Confirmation Required' : 'Authentication Warning'}
            </span>
            <span>{displayError}</span>
            {isEmailUnconfirmed && onNavigateToMailbox && (
              <div className="mt-2">
                <button
                  type="button"
                  onClick={onNavigateToMailbox}
                  className="inline-flex items-center text-xs font-semibold text-amber-800 underline hover:text-amber-900"
                >
                  Open Development Mailbox to retrieve verification link &rarr;
                </button>
              </div>
            )}
          </div>
        </div>
      )}

      <form onSubmit={handleSubmit} noValidate className="space-y-4">
        {/* Email Field */}
        <div>
          <label htmlFor="login-email" className="block text-xs font-semibold text-slate-700 uppercase tracking-wider mb-1.5">
            University Email
          </label>
          <div className="relative">
            <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-slate-400">
              <Mail className="w-4 h-4" />
            </div>
            <input
              id="login-email"
              type="email"
              autoComplete="username"
              required
              value={email}
              onChange={(e) => {
                setEmail(e.target.value);
                if (clientValidation) setClientValidation(null);
              }}
              placeholder="user@campus.internal"
              disabled={isLoading}
              className="w-full pl-9 pr-3 py-2 text-sm bg-slate-50 border border-slate-300 rounded-lg text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-sky-500 focus:bg-white transition disabled:opacity-50"
            />
          </div>
        </div>

        {/* Password Field */}
        <div>
          <div className="flex items-center justify-between mb-1.5">
            <label htmlFor="login-password" className="block text-xs font-semibold text-slate-700 uppercase tracking-wider">
              Password
            </label>
            {onNavigateToForgotPassword && (
              <button
                type="button"
                data-testid="forgot-password-link"
                onClick={onNavigateToForgotPassword}
                className="text-xs font-semibold text-sky-600 hover:text-sky-700 underline focus:outline-none cursor-pointer"
              >
                Forgot password?
              </button>
            )}
          </div>
          <div className="relative">
            <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-slate-400">
              <Lock className="w-4 h-4" />
            </div>
            <input
              id="login-password"
              type={showPassword ? 'text' : 'password'}
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => {
                setPassword(e.target.value);
                if (clientValidation) setClientValidation(null);
              }}
              placeholder="••••••••"
              disabled={isLoading}
              className="w-full pl-9 pr-10 py-2 text-sm bg-slate-50 border border-slate-300 rounded-lg text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-sky-500 focus:bg-white transition disabled:opacity-50"
            />
            <button
              type="button"
              onClick={() => setShowPassword(!showPassword)}
              aria-label={showPassword ? 'Hide password' : 'Show password'}
              className="absolute inset-y-0 right-0 pr-3 flex items-center text-slate-400 hover:text-slate-600 focus:outline-none"
            >
              {showPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
            </button>
          </div>
        </div>

        {/* Remember Me Option */}
        <div className="flex items-center justify-between pt-1">
          <label htmlFor="login-remember-me" className="flex items-center space-x-2 text-xs text-slate-600 cursor-pointer">
            <input
              id="login-remember-me"
              type="checkbox"
              checked={rememberMe}
              onChange={(e) => setRememberMe(e.target.checked)}
              disabled={isLoading}
              className="rounded border-slate-300 text-sky-600 focus:ring-sky-500 w-4 h-4"
            />
            <span>Remember session on this device</span>
          </label>
        </div>

        {/* Submit Button */}
        <button
          type="submit"
          data-testid="login-submit-btn"
          disabled={isLoading}
          aria-busy={isLoading}
          className="w-full mt-2 inline-flex items-center justify-center space-x-2 px-4 py-2.5 text-sm font-semibold text-white bg-sky-600 hover:bg-sky-700 active:bg-sky-800 disabled:opacity-60 rounded-lg shadow-sm transition cursor-pointer"
        >
          {isLoading ? (
            <>
              <Loader2 className="w-4 h-4 animate-spin" />
              <span>Verifying credentials...</span>
            </>
          ) : (
            <>
              <LogIn className="w-4 h-4" />
              <span>Sign In</span>
            </>
          )}
        </button>
      </form>

      {/* Create Account Link */}
      {onNavigateToRegister && (
        <div className="mt-4 pt-4 border-t border-slate-100 text-center">
          <p className="text-xs text-slate-500">
            Don't have an account?{' '}
            <button
              type="button"
              data-testid="create-account-link"
              onClick={onNavigateToRegister}
              className="font-semibold text-sky-600 hover:text-sky-700 underline focus:outline-none cursor-pointer"
            >
              Create account
            </button>
          </p>
        </div>
      )}

      <div className="mt-4 pt-3 border-t border-slate-100 text-center">
        <p className="text-xs text-slate-400">
          Security: Protected via Same-Origin HttpOnly cookie session and anti-CSRF request tokens.
        </p>
      </div>
    </div>
  );
};
