import React, { useState } from 'react';
import { Eye, EyeOff, Loader2, AlertCircle, Check, X, ShieldCheck } from 'lucide-react';
import { registerUser } from './api';

interface RegisterFormProps {
  onSuccess: (email: string) => void;
  onNavigateToLogin: () => void;
}

export const RegisterForm: React.FC<RegisterFormProps> = ({ onSuccess, onNavigateToLogin }) => {
  const [email, setEmail] = useState<string>('');
  const [password, setPassword] = useState<string>('');
  const [confirmPassword, setConfirmPassword] = useState<string>('');
  const [showPassword, setShowPassword] = useState<boolean>(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState<boolean>(false);
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Real-time password requirement evaluations
  const checks = {
    length: password.length >= 8,
    upper: /[A-Z]/.test(password),
    lower: /[a-z]/.test(password),
    digit: /\d/.test(password),
    special: /[^A-Za-z0-9]/.test(password),
    match: password.length > 0 && confirmPassword.length > 0 && password === confirmPassword,
  };

  const isFormValid =
    email.trim().includes('@') &&
    checks.length &&
    checks.upper &&
    checks.lower &&
    checks.digit &&
    checks.special &&
    checks.match;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMessage(null);

    if (!email.trim() || !password || !confirmPassword) {
      setErrorMessage('Please fill in all required registration fields.');
      return;
    }

    if (password !== confirmPassword) {
      setErrorMessage('Password and confirmation password do not match.');
      return;
    }

    setIsSubmitting(true);
    try {
      await registerUser({
        email: email.trim(),
        password,
        confirmPassword,
      });

      onSuccess(email.trim());
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Registration failed. Please check requirements.';
      setErrorMessage(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="w-full max-w-md mx-auto">
      <div className="bg-white border border-slate-200 rounded-2xl shadow-sm p-6 sm:p-8 space-y-6">
        <div className="text-center space-y-2">
          <div className="inline-flex items-center justify-center w-12 h-12 rounded-xl bg-sky-50 text-sky-600 mb-1 border border-sky-100">
            <ShieldCheck className="w-6 h-6" />
          </div>
          <h2 className="text-2xl font-bold text-slate-900 tracking-tight">Create your account</h2>
          <p className="text-xs text-slate-500">
            Sign up to reserve university auditoriums, seminar halls, and spaces
          </p>
        </div>

        {errorMessage && (
          <div
            role="alert"
            data-testid="register-error-banner"
            className="flex items-start space-x-3 p-3.5 rounded-lg bg-rose-50 border border-rose-200 text-rose-800 text-xs animate-in fade-in"
          >
            <AlertCircle className="w-4 h-4 text-rose-600 shrink-0 mt-0.5" />
            <div className="flex-1 font-medium">{errorMessage}</div>
          </div>
        )}

        <form onSubmit={handleSubmit} noValidate className="space-y-4">
          {/* Email Field */}
          <div className="space-y-1.5">
            <label htmlFor="reg-email" className="block text-xs font-semibold text-slate-700">
              University Email Address <span className="text-rose-500">*</span>
            </label>
            <input
              id="reg-email"
              type="email"
              autoComplete="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              disabled={isSubmitting}
              placeholder="e.g. yourname@campus.internal"
              className="w-full px-3.5 py-2 text-sm bg-white border border-slate-300 rounded-lg shadow-2xs placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-sky-500/20 focus:border-sky-500 transition disabled:opacity-50"
            />
          </div>

          {/* Password Field */}
          <div className="space-y-1.5">
            <label htmlFor="reg-password" className="block text-xs font-semibold text-slate-700">
              Password <span className="text-rose-500">*</span>
            </label>
            <div className="relative">
              <input
                id="reg-password"
                type={showPassword ? 'text' : 'password'}
                autoComplete="new-password"
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                disabled={isSubmitting}
                placeholder="Choose a strong password"
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
            <label htmlFor="reg-confirm-password" className="block text-xs font-semibold text-slate-700">
              Confirm Password <span className="text-rose-500">*</span>
            </label>
            <div className="relative">
              <input
                id="reg-confirm-password"
                type={showConfirmPassword ? 'text' : 'password'}
                autoComplete="new-password"
                required
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                disabled={isSubmitting}
                placeholder="Re-enter your password"
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
            data-testid="register-submit-btn"
            disabled={isSubmitting || !isFormValid}
            className="w-full flex items-center justify-center space-x-2 py-2.5 px-4 bg-sky-600 hover:bg-sky-700 text-white text-sm font-semibold rounded-lg shadow-sm transition disabled:opacity-50 disabled:cursor-not-allowed cursor-pointer focus:outline-none focus:ring-2 focus:ring-sky-500 focus:ring-offset-2"
          >
            {isSubmitting ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                <span>Creating account...</span>
              </>
            ) : (
              <span>Create Account</span>
            )}
          </button>
        </form>

        <div className="pt-2 text-center border-t border-slate-100">
          <p className="text-xs text-slate-500">
            Already have an account?{' '}
            <button
              type="button"
              onClick={onNavigateToLogin}
              className="font-semibold text-sky-600 hover:text-sky-700 underline focus:outline-none"
            >
              Sign In
            </button>
          </p>
        </div>
      </div>
    </div>
  );
};
