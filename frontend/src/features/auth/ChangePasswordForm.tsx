import React, { useState } from 'react';
import { Eye, EyeOff, Loader2, AlertCircle, Check, X, KeyRound, ShieldCheck } from 'lucide-react';
import { changePassword } from './api';

interface ChangePasswordFormProps {
  onSuccess: (successMessage: string) => void;
  onCancel: () => void;
}

export const ChangePasswordForm: React.FC<ChangePasswordFormProps> = ({
  onSuccess,
  onCancel,
}) => {
  const [currentPassword, setCurrentPassword] = useState<string>('');
  const [newPassword, setNewPassword] = useState<string>('');
  const [confirmPassword, setConfirmPassword] = useState<string>('');
  const [showCurrentPassword, setShowCurrentPassword] = useState<boolean>(false);
  const [showNewPassword, setShowNewPassword] = useState<boolean>(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState<boolean>(false);
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

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
    currentPassword.length > 0 &&
    checks.length &&
    checks.upper &&
    checks.lower &&
    checks.digit &&
    checks.special &&
    checks.match;

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setErrorMessage(null);

    if (!currentPassword) {
      setErrorMessage('Please enter your current account password.');
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
      const response = await changePassword({
        currentPassword,
        newPassword,
        confirmPassword,
      });

      onSuccess(response.message || 'Password has been successfully changed. Please sign in with your new password.');
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Password change failed.';
      setErrorMessage(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="bg-white border border-slate-200 rounded-2xl shadow-sm p-6 sm:p-8 space-y-6">
      <div className="flex items-center space-x-3 pb-4 border-b border-slate-100">
        <div className="w-10 h-10 rounded-xl bg-sky-50 text-sky-600 flex items-center justify-center border border-sky-100 shadow-2xs">
          <KeyRound className="w-5 h-5" />
        </div>
        <div>
          <h3 className="text-base font-bold text-slate-900 tracking-tight">Change Account Password</h3>
          <p className="text-xs text-slate-500">
            Update your university portal credentials. Active sessions will require re-authentication.
          </p>
        </div>
      </div>

      {errorMessage && (
        <div
          role="alert"
          data-testid="change-password-error-banner"
          className="flex items-start space-x-3 p-3.5 rounded-lg bg-rose-50 border border-rose-200 text-rose-800 text-xs animate-in fade-in"
        >
          <AlertCircle className="w-4 h-4 text-rose-600 shrink-0 mt-0.5" />
          <div className="flex-1 font-medium">{errorMessage}</div>
        </div>
      )}

      <form onSubmit={handleSubmit} noValidate className="space-y-4">
        {/* Current Password Field */}
        <div className="space-y-1.5">
          <label htmlFor="change-current-password" className="block text-xs font-semibold text-slate-700">
            Current Password <span className="text-rose-500">*</span>
          </label>
          <div className="relative">
            <input
              id="change-current-password"
              type={showCurrentPassword ? 'text' : 'password'}
              autoComplete="current-password"
              required
              value={currentPassword}
              onChange={(e) => {
                setCurrentPassword(e.target.value);
                if (errorMessage) setErrorMessage(null);
              }}
              disabled={isSubmitting}
              placeholder="Enter your current password"
              className="w-full px-3.5 py-2 pr-10 text-sm bg-white border border-slate-300 rounded-lg shadow-2xs placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-sky-500/20 focus:border-sky-500 transition disabled:opacity-50"
            />
            <button
              type="button"
              onClick={() => setShowCurrentPassword(!showCurrentPassword)}
              className="absolute right-2.5 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600 p-1"
              aria-label={showCurrentPassword ? 'Hide current password' : 'Show current password'}
            >
              {showCurrentPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
            </button>
          </div>
        </div>

        {/* New Password Field */}
        <div className="space-y-1.5">
          <label htmlFor="change-new-password" className="block text-xs font-semibold text-slate-700">
            New Password <span className="text-rose-500">*</span>
          </label>
          <div className="relative">
            <input
              id="change-new-password"
              type={showNewPassword ? 'text' : 'password'}
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
              onClick={() => setShowNewPassword(!showNewPassword)}
              className="absolute right-2.5 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600 p-1"
              aria-label={showNewPassword ? 'Hide new password' : 'Show new password'}
            >
              {showNewPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
            </button>
          </div>
        </div>

        {/* Confirm New Password Field */}
        <div className="space-y-1.5">
          <label htmlFor="change-confirm-password" className="block text-xs font-semibold text-slate-700">
            Confirm New Password <span className="text-rose-500">*</span>
          </label>
          <div className="relative">
            <input
              id="change-confirm-password"
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
          <div className="flex items-center space-x-1.5 font-semibold text-slate-700 mb-1">
            <ShieldCheck className="w-3.5 h-3.5 text-sky-600" />
            <span>New Password Requirements:</span>
          </div>
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

        {/* Action Buttons */}
        <div className="flex flex-col sm:flex-row items-center gap-3 pt-2">
          <button
            type="submit"
            data-testid="change-password-submit-btn"
            disabled={isSubmitting || !isFormValid}
            className="w-full sm:flex-1 flex items-center justify-center space-x-2 py-2.5 px-4 bg-sky-600 hover:bg-sky-700 text-white text-sm font-semibold rounded-lg shadow-sm transition disabled:opacity-50 disabled:cursor-not-allowed cursor-pointer focus:outline-none focus:ring-2 focus:ring-sky-500 focus:ring-offset-2"
          >
            {isSubmitting ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                <span>Updating password...</span>
              </>
            ) : (
              <span>Update Password</span>
            )}
          </button>
          <button
            type="button"
            data-testid="change-password-cancel-btn"
            disabled={isSubmitting}
            onClick={onCancel}
            className="w-full sm:w-auto px-4 py-2.5 text-sm font-semibold text-slate-600 hover:text-slate-900 bg-slate-100 hover:bg-slate-200 rounded-lg transition disabled:opacity-50 cursor-pointer"
          >
            Cancel
          </button>
        </div>
      </form>
    </div>
  );
};
