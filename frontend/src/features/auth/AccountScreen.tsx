import React, { useState } from 'react';
import { ShieldCheck, LogOut, RefreshCw, KeyRound, CheckCircle2, Clock, Lock, AlertCircle, Loader2 } from 'lucide-react';
import { useAuth } from './useAuth';
import { ChangePasswordForm } from './ChangePasswordForm';
import { signOutEverywhere } from './api';

interface AccountScreenProps {
  onPasswordChanged?: (message: string) => void;
  onSignedOutEverywhere?: (message: string) => void;
}

export const AccountScreen: React.FC<AccountScreenProps> = ({
  onPasswordChanged,
  onSignedOutEverywhere,
}) => {
  const { user, logout, checkSession, clearSession, isLoading } = useAuth();
  const [refreshing, setRefreshing] = useState(false);
  const [lastVerified, setLastVerified] = useState<Date>(new Date());
  const [showChangePassword, setShowChangePassword] = useState<boolean>(false);
  const [showSignOutEverywhereDialog, setShowSignOutEverywhereDialog] = useState<boolean>(false);
  const [isSigningOutEverywhere, setIsSigningOutEverywhere] = useState<boolean>(false);
  const [signOutEverywhereError, setSignOutEverywhereError] = useState<string | null>(null);

  if (!user) {
    return null;
  }

  const handleRefresh = async () => {
    setRefreshing(true);
    await checkSession();
    setLastVerified(new Date());
    setRefreshing(false);
  };

  const handleSignOutEverywhere = async () => {
    setIsSigningOutEverywhere(true);
    setSignOutEverywhereError(null);
    try {
      const response = await signOutEverywhere();
      clearSession();
      setShowSignOutEverywhereDialog(false);
      onSignedOutEverywhere?.(response.message || 'You have been signed out from all devices successfully.');
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to sign out from all devices.';
      setSignOutEverywhereError(msg);
    } finally {
      setIsSigningOutEverywhere(false);
    }
  };

  return (
    <div className="w-full max-w-2xl mx-auto space-y-6">
      {/* Account Overview Header Card */}
      <div className="bg-white border border-slate-200 rounded-2xl p-6 sm:p-8 shadow-sm">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between pb-6 border-b border-slate-100 gap-4">
          <div className="flex items-center space-x-4">
            <div className="w-14 h-14 rounded-2xl bg-sky-600 text-white flex items-center justify-center font-bold text-xl shadow-xs">
              {user.email.substring(0, 2).toUpperCase()}
            </div>
            <div>
              <div className="flex items-center space-x-2">
                <h2 className="text-xl font-extrabold text-slate-900 tracking-tight">{user.email}</h2>
                <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold bg-emerald-100 text-emerald-800 border border-emerald-200">
                  <CheckCircle2 className="w-3 h-3 mr-1 text-emerald-600" />
                  Authenticated
                </span>
              </div>
              <p className="text-xs text-slate-500 mt-1 font-mono">ID: {user.id}</p>
            </div>
          </div>

          <button
            onClick={logout}
            disabled={isLoading}
            className="inline-flex items-center justify-center space-x-2 px-4 py-2 text-xs font-semibold text-rose-700 bg-rose-50 hover:bg-rose-100 border border-rose-200 rounded-lg transition disabled:opacity-50"
          >
            <LogOut className="w-4 h-4" />
            <span>Sign Out</span>
          </button>
        </div>

        {/* Security & Session Metadata */}
        <div className="mt-6 grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div className="p-4 rounded-xl border border-slate-100 bg-slate-50 space-y-1.5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-slate-500 uppercase tracking-wider">Session Security</span>
              <ShieldCheck className="w-4 h-4 text-emerald-600" />
            </div>
            <p className="text-sm font-bold text-slate-800">Same-Origin HttpOnly Cookie</p>
            <p className="text-xs text-slate-500 leading-relaxed">
              No authentication tokens stored in localStorage. Cookies are protected against script extraction.
            </p>
          </div>

          <div className="p-4 rounded-xl border border-slate-100 bg-slate-50 space-y-1.5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-slate-500 uppercase tracking-wider">CSRF Protection</span>
              <KeyRound className="w-4 h-4 text-sky-600" />
            </div>
            <p className="text-sm font-bold text-slate-800">Dynamic Anti-CSRF Token</p>
            <p className="text-xs text-slate-500 leading-relaxed">
              State-altering actions require anti-forgery request header validation on the server.
            </p>
          </div>
        </div>

        {/* Live Session Verification Control */}
        <div className="mt-6 pt-5 border-t border-slate-100 flex flex-col sm:flex-row sm:items-center justify-between text-xs text-slate-500 gap-3">
          <div className="flex items-center space-x-1.5">
            <Clock className="w-3.5 h-3.5 text-slate-400" />
            <span>Session verified at {lastVerified.toLocaleTimeString()} via <code className="font-mono text-sky-700">/api/auth/me</code></span>
          </div>

          <button
            onClick={handleRefresh}
            disabled={refreshing || isLoading}
            className="inline-flex items-center space-x-1 px-3 py-1 bg-slate-100 hover:bg-slate-200 text-slate-700 rounded-md transition disabled:opacity-50 font-medium"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${refreshing ? 'animate-spin' : ''}`} />
            <span>Verify Active Session</span>
          </button>
        </div>
      </div>

      {/* Security & Password Management */}
      {showChangePassword ? (
        <ChangePasswordForm
          onSuccess={(msg) => {
            clearSession();
            setShowChangePassword(false);
            onPasswordChanged?.(msg);
          }}
          onCancel={() => setShowChangePassword(false)}
        />
      ) : (
        <div className="bg-white border border-slate-200 rounded-2xl p-6 shadow-sm flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div className="flex items-center space-x-3.5">
            <div className="w-10 h-10 rounded-xl bg-slate-100 text-slate-700 flex items-center justify-center border border-slate-200">
              <Lock className="w-5 h-5 text-slate-600" />
            </div>
            <div>
              <h3 className="text-sm font-bold text-slate-900">Account Password</h3>
              <p className="text-xs text-slate-500">
                Change your password to keep your account secure.
              </p>
            </div>
          </div>
          <button
            type="button"
            data-testid="open-change-password-btn"
            onClick={() => setShowChangePassword(true)}
            className="inline-flex items-center justify-center space-x-2 px-4 py-2 text-xs font-semibold text-slate-700 bg-slate-100 hover:bg-slate-200 border border-slate-200 rounded-lg transition cursor-pointer"
          >
            <KeyRound className="w-4 h-4 text-slate-500" />
            <span>Change Password</span>
          </button>
        </div>
      )}

      {/* Sign Out Everywhere Section */}
      <div className="bg-white border border-slate-200 rounded-2xl p-6 shadow-sm flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="flex items-center space-x-3.5">
          <div className="w-10 h-10 rounded-xl bg-rose-50 text-rose-700 flex items-center justify-center border border-rose-100">
            <LogOut className="w-5 h-5 text-rose-600" />
          </div>
          <div>
            <h3 className="text-sm font-bold text-slate-900">Sign Out Everywhere</h3>
            <p className="text-xs text-slate-500">
              Revoke all active sessions on other browsers and devices.
            </p>
          </div>
        </div>
        <button
          type="button"
          data-testid="open-sign-out-everywhere-btn"
          onClick={() => setShowSignOutEverywhereDialog(true)}
          className="inline-flex items-center justify-center space-x-2 px-4 py-2 text-xs font-semibold text-rose-700 bg-rose-50 hover:bg-rose-100 border border-rose-200 rounded-lg transition cursor-pointer"
        >
          <LogOut className="w-4 h-4 text-rose-600" />
          <span>Sign Out Everywhere</span>
        </button>
      </div>

      {/* Confirmation Dialog for Sign Out Everywhere */}
      {showSignOutEverywhereDialog && (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="soe-dialog-title"
          className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/40 backdrop-blur-xs animate-in fade-in"
        >
          <div className="bg-white border border-slate-200 rounded-2xl p-6 sm:p-8 max-w-md w-full shadow-xl space-y-4">
            <div className="flex items-start space-x-3.5">
              <div className="w-10 h-10 rounded-xl bg-amber-100 text-amber-700 flex items-center justify-center shrink-0">
                <AlertCircle className="w-5 h-5 text-amber-600" />
              </div>
              <div className="space-y-1">
                <h3 id="soe-dialog-title" className="text-base font-bold text-slate-900">
                  Sign out from all devices?
                </h3>
                <p className="text-xs text-slate-600 leading-relaxed">
                  This action will immediately terminate your session on this device and invalidate all other active sessions across any browsers or mobile devices.
                </p>
                <p className="text-xs text-amber-800 bg-amber-50 p-2.5 rounded-lg border border-amber-200 mt-2 font-medium">
                  Note: Other active sessions may take up to 5 minutes to be rejected on their next request.
                </p>
              </div>
            </div>

            {signOutEverywhereError && (
              <div
                role="alert"
                data-testid="soe-error-banner"
                className="flex items-start space-x-2.5 p-3 rounded-lg bg-rose-50 border border-rose-200 text-rose-800 text-xs"
              >
                <AlertCircle className="w-4 h-4 text-rose-600 shrink-0 mt-0.5" />
                <div className="flex-1 font-medium">{signOutEverywhereError}</div>
              </div>
            )}

            <div className="flex flex-col-reverse sm:flex-row items-center justify-end gap-2.5 pt-3 border-t border-slate-100">
              <button
                type="button"
                data-testid="cancel-sign-out-everywhere-btn"
                disabled={isSigningOutEverywhere}
                onClick={() => {
                  setShowSignOutEverywhereDialog(false);
                  setSignOutEverywhereError(null);
                }}
                className="w-full sm:w-auto px-4 py-2 text-xs font-semibold text-slate-700 hover:text-slate-900 bg-slate-100 hover:bg-slate-200 rounded-lg transition disabled:opacity-50 cursor-pointer"
              >
                Cancel
              </button>
              <button
                type="button"
                data-testid="confirm-sign-out-everywhere-btn"
                disabled={isSigningOutEverywhere}
                onClick={handleSignOutEverywhere}
                className="w-full sm:w-auto inline-flex items-center justify-center space-x-1.5 px-4 py-2 text-xs font-semibold text-white bg-rose-600 hover:bg-rose-700 rounded-lg shadow-sm transition disabled:opacity-50 cursor-pointer"
              >
                {isSigningOutEverywhere ? (
                  <>
                    <Loader2 className="w-3.5 h-3.5 animate-spin" />
                    <span>Signing out everywhere...</span>
                  </>
                ) : (
                  <span>Yes, Sign Out Everywhere</span>
                )}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
