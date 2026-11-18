import React, { useState, useEffect } from 'react';
import { Eye, EyeOff, Loader2, AlertCircle, Check, X, ShieldAlert, CheckCircle2, ArrowLeft } from 'lucide-react';
import { resetPassword } from './api';

interface ResetPasswordFormProps {
  userId: string;
  token: string;
  onSuccess: (successMessage: string) => void;
  onNavigateToLogin: () => void;
  onRequestNewLink: () => void;
}

export const ResetPasswordForm: React.FC<ResetPasswordFormProps> = ({
  userId,
  token,
  onSuccess,
  onNavigateToLogin,
  onRequestNewLink,
}) => {
  const [newPassword, setNewPassword] = useState<string>('');
  const [confirmPassword, setConfirmPassword] = useState<string>('');
  const [showPassword, setShowPassword] = useState<boolean>(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState<boolean>(false);
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isInvalidToken, setIsInvalidToken] = useState<boolean>(false);

  // Clean sensitive reset parameters out of the browser address bar and fragment once loaded
  useEffect(() => {
    if (typeof window !== 'undefined') {
      if (window.location.hash.includes('token=') || window.location.search.includes('token=')) {
        const cleanUrl = window.location.pathname + '?tab=auth';
        window.history.replaceState({}, document.title, cleanUrl);
      }
    }
  }, []);

  const hasMissingParameters = !userId || !token;

  // Real-time password requirement evaluations
  const checks = {
    length: newPassword.length >= 8,
    upper: /[A-Z]/.test(newPassword),
    lower: /[a-z]/.test(newPassword),
    digit: /\d/.test(newPassword),
    special: /[^A-Za-z0-9]/.test(newPassword),
    match: newPassword.length > 0 && confirmPassword.length > 0 && newPassword === confirmPassword,
  };

  const isFormValid =
    checks.length &&
    checks.upper &&
    checks.lower &&
    checks.digit &&
    checks.special &&
    checks.match;

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setErrorMessage(null);

    if (hasMissingParameters) {
      setIsInvalidToken(true);
      return;
    }

    if (!newPassword || !confirmPassword) {
      setErrorMessage('Please enter and confirm your new password.');
      return;
    }

    if (newPassword !== confirmPassword) {
      setErrorMessage('New password and confirmation password do not match.');
      return;
    }

    setIsSubmitting(true);
    try {
      const response = await resetPassword({
        userId,
        token,
        newPassword,
        confirmPassword,
      });

      onSuccess(response.message || 'Password has been successfully reset. You may now sign in.');
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Password reset failed.';
      if (msg.toLowerCase().includes('invalid or expired')) {
        setIsInvalidToken(true);
      } else {
        setErrorMessage(msg);
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  if (hasMissingParameters || isInvalidToken) {
    return (
      <div className="w-full max-w-md mx-auto bg-white border border-slate-200 rounded-2xl shadow-sm p-6 sm:p-8 space-y-6">
        <div className="text-center space-y-2">
          <div className="w-12 h-12 bg-amber-100 text-amber-700 rounded-xl flex items-center justify-center mx-auto shadow-xs">
            <ShieldAlert className="w-6 h-6 text-amber-600" />
          </div>
          <h2 className="text-xl font-bold text-slate-900 tracking-tight">Invalid or Expired Link</h2>
          <p className="text-xs text-slate-600 leading-relaxed">
            This password reset link is invalid, has expired, or has already been used. For your security, password reset links can only be used once.
          </p>
        </div>

        <div className="space-y-3 pt-2">
          <button
            type="button"
            data-testid="request-new-link-btn"
            onClick={onRequestNewLink}
            className="w-full inline-flex items-center justify-center space-x-2 px-4 py-2.5 text-sm font-semibold text-white bg-sky-600 hover:bg-sky-700 rounded-lg shadow-sm transition cursor-pointer"
          >
            <span>Request New Reset Link</span>
          </button>
          <button
            type="button"
            onClick={onNavigateToLogin}
            className="w-full inline-flex items-center justify-center space-x-2 px-4 py-2 text-xs font-semibold text-slate-600 hover:text-slate-900 transition cursor-pointer"
          >
            <ArrowLeft className="w-3.5 h-3.5" />
            <span>Return to Sign In</span>
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="w-full max-w-md mx-auto">
      <div className="bg-white border border-slate-200 rounded-2xl shadow-sm p-6 sm:p-8 space-y-6">
        <div className="text-center space-y-2">
          <div className="inline-flex items-center justify-center w-12 h-12 rounded-xl bg-sky-50 text-sky-600 mb-1 border border-sky-100">
            <CheckCircle2 className="w-6 h-6" />
          </div>
          <h2 className="text-2xl font-bold text-slate-900 tracking-tight">Choose a new password</h2>
          <p className="text-xs text-slate-500">
            Create a strong, secure password for your CampusVenue university account.
          </p>
        </div>

        {errorMessage && (
          <div
            role="alert"
            data-testid="reset-password-error-banner"
            className="flex items-start space-x-3 p-3.5 rounded-lg bg-rose-50 border border-rose-200 text-rose-800 text-xs animate-in fade-in"
          >
            <AlertCircle className="w-4 h-4 text-rose-600 shrink-0 mt-0.5" />
            <div className="flex-1 font-medium">{errorMessage}</div>
          </div>
        )}

        <form onSubmit={handleSubmit} noValidate className="space-y-4">
          {/* New Password Field */}
          <div className="space-y-1.5">
            <label htmlFor="reset-new-password" className="block text-xs font-semibold text-slate-700">
              New Password <span className="text-rose-500">*</span>
            </label>
            <div className="relative">
              <input
                id="reset-new-password"
                type={showPassword ? 'text' : 'password'}
                autoComplete="new-password"
                required
                value={newPassword}
                onChange={(e) => {
                  setNewPassword(e.target.value);
                  if (errorMessage) setErrorMessage(null);
                }}
                disabled={isSubmitting}
                placeholder="Enter new strong password"
                className="w-full px-3.5 py-2 pr-10 text-sm bg-white border border-slate-300 rounded-lg shadow-2xs placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-sky-500/20 focus:border-sky-500 transition disabled:opacity-50"
              />
              <button
                type="button"
                onClick={() => setShowPassword(!showPassword)}
                className="absolute right-2.5 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600 p-1"
                aria-label={showPassword ? 'Hide password' : 'Show password'}
              >
                {showPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
              </button>
            </div>
          </div>

          {/* Confirm Password Field */}
          <div className="space-y-1.5">
            <label htmlFor="reset-confirm-password" className="block text-xs font-semibold text-slate-700">
              Confirm New Password <span className="text-rose-500">*</span>
            </label>
            <div className="relative">
              <input
                id="reset-confirm-password"
                type={showConfirmPassword ? 'text' : 'password'}
                autoComplete="new-password"
                required
                value={confirmPassword}
                onChange={(e) => {
                  setConfirmPassword(e.target.value);
                  if (errorMessage) setErrorMessage(null);
                }}
                disabled={isSubmitting}
                placeholder="Re-enter your new password"
                className="w-full px-3.5 py-2 pr-10 text-sm bg-white border border-slate-300 rounded-lg shadow-2xs placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-sky-500/20 focus:border-sky-500 transition disabled:opacity-50"
              />
              <button
                type="button"
                onClick={() => setShowConfirmPassword(!showConfirmPassword)}
                className="absolute right-2.5 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600 p-1"
                aria-label={showConfirmPassword ? 'Hide confirm password' : 'Show confirm password'}
              >
                {showConfirmPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
              </button>
            </div>
          </div>

          {/* Password Requirements Checklist */}
          <div className="p-3 bg-slate-50 border border-slate-200 rounded-lg space-y-1.5 text-xs text-slate-600">
            <p className="font-semibold text-slate-700 mb-1">Password Requirements:</p>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-1.5">
              <span className={`flex items-center space-x-1.5 ${checks.length ? 'text-emerald-700 font-medium' : 'text-slate-500'}`}>
                {checks.length ? <Check className="w-3.5 h-3.5 text-emerald-600" /> : <X className="w-3.5 h-3.5 text-slate-400" />}
                <span>8+ characters</span>
              </span>
              <span className={`flex items-center space-x-1.5 ${checks.upper ? 'text-emerald-700 font-medium' : 'text-slate-500'}`}>
                {checks.upper ? <Check className="w-3.5 h-3.5 text-emerald-600" /> : <X className="w-3.5 h-3.5 text-slate-400" />}
                <span>Uppercase letter</span>
              </span>
              <span className={`flex items-center space-x-1.5 ${checks.lower ? 'text-emerald-700 font-medium' : 'text-slate-500'}`}>
                {checks.lower ? <Check className="w-3.5 h-3.5 text-emerald-600" /> : <X className="w-3.5 h-3.5 text-slate-400" />}
                <span>Lowercase letter</span>
              </span>
              <span className={`flex items-center space-x-1.5 ${checks.digit ? 'text-emerald-700 font-medium' : 'text-slate-500'}`}>
                {checks.digit ? <Check className="w-3.5 h-3.5 text-emerald-600" /> : <X className="w-3.5 h-3.5 text-slate-400" />}
                <span>Number (0-9)</span>
              </span>
              <span className={`flex items-center space-x-1.5 ${checks.special ? 'text-emerald-700 font-medium' : 'text-slate-500'}`}>
                {checks.special ? <Check className="w-3.5 h-3.5 text-emerald-600" /> : <X className="w-3.5 h-3.5 text-slate-400" />}
                <span>Special character (!@#$)</span>
              </span>
              <span className={`flex items-center space-x-1.5 ${checks.match ? 'text-emerald-700 font-medium' : 'text-slate-500'}`}>
                {checks.match ? <Check className="w-3.5 h-3.5 text-emerald-600" /> : <X className="w-3.5 h-3.5 text-slate-400" />}
                <span>Passwords match</span>
              </span>
            </div>
          </div>

          {/* Submit Button */}
          <button
            type="submit"
            data-testid="reset-password-submit-btn"
            disabled={isSubmitting || !isFormValid}
            className="w-full flex items-center justify-center space-x-2 py-2.5 px-4 bg-sky-600 hover:bg-sky-700 text-white text-sm font-semibold rounded-lg shadow-sm transition disabled:opacity-50 disabled:cursor-not-allowed cursor-pointer focus:outline-none focus:ring-2 focus:ring-sky-500 focus:ring-offset-2"
          >
            {isSubmitting ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                <span>Updating password...</span>
              </>
            ) : (
              <span>Reset Password</span>
            )}
          </button>
        </form>

        <div className="pt-2 text-center border-t border-slate-100">
          <button
            type="button"
            onClick={onNavigateToLogin}
            className="text-xs font-semibold text-slate-600 hover:text-slate-900 transition focus:outline-none cursor-pointer"
          >
            Cancel and Return to Sign In
          </button>
        </div>
      </div>
    </div>
  );
};
