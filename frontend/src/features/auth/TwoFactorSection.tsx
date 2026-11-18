import React, { useState, useEffect, useCallback } from 'react';
import {
  ShieldCheck,
  ShieldAlert,
  KeyRound,
  CheckCircle2,
  AlertCircle,
  Copy,
  Check,
  Loader2,
  Lock,
  QrCode,
  Eye,
  EyeOff,
  RefreshCw,
} from 'lucide-react';
import type { TwoFactorStatusDto, TwoFactorSetupDto } from './types';
import {
  getTwoFactorStatus,
  getTwoFactorSetup,
  enableTwoFactor,
  disableTwoFactor,
  regenerateRecoveryCodes,
} from './api';

interface TwoFactorSectionProps {
  onStatusChange?: (isEnabled: boolean) => void;
}

export const TwoFactorSection: React.FC<TwoFactorSectionProps> = ({ onStatusChange }) => {
  const [status, setStatus] = useState<TwoFactorStatusDto | null>(null);
  const [loadingStatus, setLoadingStatus] = useState<boolean>(true);
  const [statusError, setStatusError] = useState<string | null>(null);

  // Enrollment modal state
  const [showEnrollModal, setShowEnrollModal] = useState<boolean>(false);
  const [setupData, setSetupData] = useState<TwoFactorSetupDto | null>(null);
  const [loadingSetup, setLoadingSetup] = useState<boolean>(false);
  const [enrollPassword, setEnrollPassword] = useState<string>('');
  const [enrollCode, setEnrollCode] = useState<string>('');
  const [showEnrollPassword, setShowEnrollPassword] = useState<boolean>(false);
  const [enrollSubmitting, setEnrollSubmitting] = useState<boolean>(false);
  const [enrollError, setEnrollError] = useState<string | null>(null);
  const [copiedKey, setCopiedKey] = useState<boolean>(false);

  // Recovery codes display modal (shown once after enroll or regenerate)
  const [displayRecoveryCodes, setDisplayRecoveryCodes] = useState<string[] | null>(null);
  const [copiedRecoveryCodes, setCopiedRecoveryCodes] = useState<boolean>(false);

  // Regenerate codes modal state
  const [showRegenModal, setShowRegenModal] = useState<boolean>(false);
  const [regenPassword, setRegenPassword] = useState<string>('');
  const [regenCode, setRegenCode] = useState<string>('');
  const [showRegenPassword, setShowRegenPassword] = useState<boolean>(false);
  const [regenSubmitting, setRegenSubmitting] = useState<boolean>(false);
  const [regenError, setRegenError] = useState<string | null>(null);

  // Disable modal state
  const [showDisableModal, setShowDisableModal] = useState<boolean>(false);
  const [disablePassword, setDisablePassword] = useState<string>('');
  const [disableCode, setDisableCode] = useState<string>('');
  const [showDisablePassword, setShowDisablePassword] = useState<boolean>(false);
  const [disableSubmitting, setDisableSubmitting] = useState<boolean>(false);
  const [disableError, setDisableError] = useState<string | null>(null);

  // Notification banners
  const [successBanner, setSuccessBanner] = useState<string | null>(null);

  const fetchStatus = useCallback(async () => {
    setLoadingStatus(true);
    setStatusError(null);
    try {
      const data = await getTwoFactorStatus();
      setStatus(data);
      onStatusChange?.(data.isTwoFactorEnabled);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to retrieve two-factor status.';
      setStatusError(msg);
    } finally {
      setLoadingStatus(false);
    }
  }, [onStatusChange]);

  useEffect(() => {
    let ignore = false;

    async function loadInitialStatus() {
      try {
        const data = await getTwoFactorStatus();
        if (!ignore) {
          setStatus(data);
          onStatusChange?.(data.isTwoFactorEnabled);
        }
      } catch (err: unknown) {
        if (!ignore) {
          const msg = err instanceof Error ? err.message : 'Failed to retrieve two-factor status.';
          setStatusError(msg);
        }
      } finally {
        if (!ignore) {
          setLoadingStatus(false);
        }
      }
    }

    loadInitialStatus();

    return () => {
      ignore = true;
    };
  }, [onStatusChange]);

  // Handle opening enrollment modal
  const handleOpenEnroll = async () => {
    setShowEnrollModal(true);
    setLoadingSetup(true);
    setEnrollError(null);
    setEnrollPassword('');
    setEnrollCode('');
    setCopiedKey(false);
    try {
      const data = await getTwoFactorSetup();
      setSetupData(data);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to initiate authenticator setup.';
      setEnrollError(msg);
    } finally {
      setLoadingSetup(false);
    }
  };

  const handleCopyKey = () => {
    if (setupData?.unformattedKey) {
      navigator.clipboard.writeText(setupData.unformattedKey).catch(() => {});
      setCopiedKey(true);
      setTimeout(() => setCopiedKey(false), 2000);
    }
  };

  const handleCopyRecoveryCodes = () => {
    if (displayRecoveryCodes && displayRecoveryCodes.length > 0) {
      const content = [
        'CampusVenue Recovery Codes',
        'Save these codes in a secure location. Each code can only be used once.',
        '',
        ...displayRecoveryCodes,
      ].join('\n');
      navigator.clipboard.writeText(content).catch(() => {});
      setCopiedRecoveryCodes(true);
      setTimeout(() => setCopiedRecoveryCodes(false), 2000);
    }
  };

  // Submit enrollment
  const handleEnrollSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setEnrollError(null);

    if (!enrollPassword) {
      setEnrollError('Please enter your current account password.');
      return;
    }
    if (!enrollCode.trim()) {
      setEnrollError('Please enter the 6-digit code from your authenticator app.');
      return;
    }

    setEnrollSubmitting(true);
    try {
      const response = await enableTwoFactor({
        currentPassword: enrollPassword,
        code: enrollCode.trim(),
      });

      setShowEnrollModal(false);
      setSetupData(null);
      setEnrollPassword('');
      setEnrollCode('');
      setDisplayRecoveryCodes(response.recoveryCodes);
      setSuccessBanner('Two-Factor Authentication is now enabled on your account.');
      await fetchStatus();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to enable two-factor authentication.';
      setEnrollError(msg);
    } finally {
      setEnrollSubmitting(false);
    }
  };

  // Submit regenerate recovery codes
  const handleRegenSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setRegenError(null);

    if (!regenPassword) {
      setRegenError('Please enter your current account password.');
      return;
    }
    if (!regenCode.trim()) {
      setRegenError('Please enter a verification code from your authenticator app or a recovery code.');
      return;
    }

    setRegenSubmitting(true);
    try {
      const response = await regenerateRecoveryCodes({
        currentPassword: regenPassword,
        code: regenCode.trim(),
      });

      setShowRegenModal(false);
      setRegenPassword('');
      setRegenCode('');
      setDisplayRecoveryCodes(response.recoveryCodes);
      setSuccessBanner('New recovery codes generated. All previous recovery codes have been invalidated.');
      await fetchStatus();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to regenerate recovery codes.';
      setRegenError(msg);
    } finally {
      setRegenSubmitting(false);
    }
  };

  // Submit disable 2FA
  const handleDisableSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setDisableError(null);

    if (!disablePassword) {
      setDisableError('Please enter your current account password.');
      return;
    }
    if (!disableCode.trim()) {
      setDisableError('Please enter a verification code from your authenticator app or a recovery code.');
      return;
    }

    setDisableSubmitting(true);
    try {
      await disableTwoFactor({
        currentPassword: disablePassword,
        code: disableCode.trim(),
      });

      setShowDisableModal(false);
      setDisablePassword('');
      setDisableCode('');
      setSuccessBanner('Two-Factor Authentication has been disabled.');
      await fetchStatus();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to disable two-factor authentication.';
      setDisableError(msg);
    } finally {
      setDisableSubmitting(false);
    }
  };

  if (loadingStatus) {
    return (
      <div className="bg-white border border-slate-200 rounded-2xl p-6 shadow-sm flex items-center justify-center space-x-2 text-slate-500 py-8">
        <Loader2 className="w-5 h-5 animate-spin text-sky-600" />
        <span className="text-xs font-medium">Checking two-factor security status...</span>
      </div>
    );
  }

  const isEnabled = status?.isTwoFactorEnabled ?? false;
  const codesLeft = status?.recoveryCodesLeft ?? 0;

  return (
    <div className="space-y-4">
      {successBanner && (
        <div
          role="status"
          aria-live="polite"
          data-testid="mfa-success-banner"
          className="rounded-xl p-3.5 flex items-start space-x-3 bg-emerald-50 border border-emerald-200 text-emerald-900 text-xs animate-in fade-in"
        >
          <CheckCircle2 className="w-4 h-4 text-emerald-600 shrink-0 mt-0.5" />
          <div className="flex-1 font-medium">{successBanner}</div>
          <button
            onClick={() => setSuccessBanner(null)}
            className="text-emerald-700 hover:text-emerald-900 font-bold ml-2 cursor-pointer"
          >
            &times;
          </button>
        </div>
      )}

      {statusError && (
        <div
          role="alert"
          className="rounded-xl p-3.5 flex items-start space-x-3 bg-rose-50 border border-rose-200 text-rose-800 text-xs"
        >
          <AlertCircle className="w-4 h-4 text-rose-600 shrink-0 mt-0.5" />
          <div className="flex-1 font-medium">{statusError}</div>
          <button onClick={fetchStatus} className="text-rose-700 underline text-xs font-semibold">
            Retry
          </button>
        </div>
      )}

      {/* Main Two-Factor Management Card */}
      <div className="bg-white border border-slate-200 rounded-2xl p-6 shadow-sm flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="flex items-start sm:items-center space-x-3.5">
          <div
            className={`w-10 h-10 rounded-xl flex items-center justify-center shrink-0 border ${
              isEnabled
                ? 'bg-emerald-50 text-emerald-700 border-emerald-100'
                : 'bg-slate-100 text-slate-700 border-slate-200'
            }`}
          >
            {isEnabled ? (
              <ShieldCheck className="w-5 h-5 text-emerald-600" />
            ) : (
              <ShieldAlert className="w-5 h-5 text-slate-500" />
            )}
          </div>
          <div className="space-y-1">
            <div className="flex items-center space-x-2">
              <h3 className="text-sm font-bold text-slate-900">Two-Factor Authentication</h3>
              <span
                data-testid="mfa-status-badge"
                className={`inline-flex items-center px-2 py-0.5 rounded-full text-2xs font-bold uppercase tracking-wider ${
                  isEnabled
                    ? 'bg-emerald-100 text-emerald-800 border border-emerald-200'
                    : 'bg-slate-100 text-slate-600 border border-slate-200'
                }`}
              >
                {isEnabled ? 'Active' : 'Disabled'}
              </span>
            </div>
            <p className="text-xs text-slate-500 leading-relaxed">
              {isEnabled
                ? `Protected with an authenticator app. You have ${codesLeft} recovery codes remaining.`
                : 'Add a second verification layer using an authenticator app (Google Authenticator, Microsoft Authenticator, 1Password, etc.).'}
            </p>
            {isEnabled && codesLeft <= 2 && (
              <p className="text-2xs font-semibold text-amber-700 bg-amber-50 px-2 py-1 rounded border border-amber-200 inline-block mt-1">
                Warning: Only {codesLeft} recovery {codesLeft === 1 ? 'code' : 'codes'} remaining. Consider regenerating new codes.
              </p>
            )}
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {isEnabled ? (
            <>
              <button
                type="button"
                data-testid="open-regenerate-codes-btn"
                onClick={() => {
                  setShowRegenModal(true);
                  setRegenError(null);
                  setRegenPassword('');
                  setRegenCode('');
                }}
                className="inline-flex items-center justify-center space-x-1.5 px-3.5 py-2 text-xs font-semibold text-slate-700 bg-slate-100 hover:bg-slate-200 border border-slate-200 rounded-lg transition cursor-pointer"
              >
                <RefreshCw className="w-3.5 h-3.5 text-slate-500" />
                <span>Regenerate Codes</span>
              </button>
              <button
                type="button"
                data-testid="open-disable-2fa-btn"
                onClick={() => {
                  setShowDisableModal(true);
                  setDisableError(null);
                  setDisablePassword('');
                  setDisableCode('');
                }}
                className="inline-flex items-center justify-center space-x-1.5 px-3.5 py-2 text-xs font-semibold text-rose-700 bg-rose-50 hover:bg-rose-100 border border-rose-200 rounded-lg transition cursor-pointer"
              >
                <span>Disable 2FA</span>
              </button>
            </>
          ) : (
            <button
              type="button"
              data-testid="open-enable-2fa-btn"
              onClick={handleOpenEnroll}
              className="inline-flex items-center justify-center space-x-1.5 px-4 py-2 text-xs font-semibold text-white bg-sky-600 hover:bg-sky-700 rounded-lg shadow-sm transition cursor-pointer"
            >
              <ShieldCheck className="w-4 h-4" />
              <span>Enable Two-Factor</span>
            </button>
          )}
        </div>
      </div>

      {/* Enrollment Modal */}
      {showEnrollModal && (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="enroll-dialog-title"
          className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/40 backdrop-blur-xs animate-in fade-in"
        >
          <div className="bg-white border border-slate-200 rounded-2xl p-6 sm:p-8 max-w-lg w-full shadow-xl space-y-5 max-h-[90vh] overflow-y-auto">
            <div className="flex items-start space-x-3.5">
              <div className="w-10 h-10 rounded-xl bg-sky-100 text-sky-700 flex items-center justify-center shrink-0">
                <QrCode className="w-5 h-5 text-sky-600" />
              </div>
              <div>
                <h3 id="enroll-dialog-title" className="text-base font-bold text-slate-900">
                  Set Up Authenticator App
                </h3>
                <p className="text-xs text-slate-500 mt-0.5">
                  Scan the QR code with your authenticator app or enter the manual setup key.
                </p>
              </div>
            </div>

            {loadingSetup ? (
              <div className="py-12 flex flex-col items-center justify-center space-y-3 text-slate-500">
                <Loader2 className="w-6 h-6 animate-spin text-sky-600" />
                <span className="text-xs font-medium">Generating local security key and QR code...</span>
              </div>
            ) : setupData ? (
              <div className="space-y-4">
                {/* Locally Generated QR Code */}
                <div className="text-center p-4 bg-slate-50 rounded-xl border border-slate-200 space-y-2">
                  <img
                    src={setupData.qrCodeDataUrl}
                    alt="Authenticator QR Code"
                    data-testid="mfa-qr-code"
                    className="w-48 h-48 mx-auto rounded-lg border border-slate-200 p-2 bg-white shadow-xs"
                  />
                  <p className="text-2xs text-slate-400">
                    Generated 100% locally. No external QR services used.
                  </p>
                </div>

                {/* Manual Setup Key */}
                <div className="space-y-1.5">
                  <label className="block text-2xs font-bold uppercase tracking-wider text-slate-500">
                    Manual Setup Key
                  </label>
                  <div className="flex items-center space-x-2">
                    <code
                      data-testid="mfa-manual-key"
                      className="flex-1 px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-xs font-mono text-slate-800 break-all select-all font-semibold"
                    >
                      {setupData.manualEntryKey}
                    </code>
                    <button
                      type="button"
                      data-testid="copy-setup-key-btn"
                      onClick={handleCopyKey}
                      className="px-3 py-2 text-xs font-semibold text-slate-700 bg-slate-100 hover:bg-slate-200 border border-slate-200 rounded-lg transition inline-flex items-center space-x-1 cursor-pointer"
                    >
                      {copiedKey ? (
                        <>
                          <Check className="w-3.5 h-3.5 text-emerald-600" />
                          <span className="text-emerald-700">Copied</span>
                        </>
                      ) : (
                        <>
                          <Copy className="w-3.5 h-3.5 text-slate-500" />
                          <span>Copy</span>
                        </>
                      )}
                    </button>
                  </div>
                </div>

                {/* Verification Form */}
                <form onSubmit={handleEnrollSubmit} className="space-y-4 pt-2 border-t border-slate-100">
                  {enrollError && (
                    <div
                      role="alert"
                      data-testid="enroll-error-banner"
                      className="p-3 rounded-lg bg-rose-50 border border-rose-200 text-rose-800 text-xs flex items-start space-x-2"
                    >
                      <AlertCircle className="w-4 h-4 text-rose-600 shrink-0 mt-0.5" />
                      <div className="flex-1 font-medium">{enrollError}</div>
                    </div>
                  )}

                  <div>
                    <label
                      htmlFor="enroll-current-password"
                      className="block text-xs font-semibold text-slate-700 uppercase tracking-wider mb-1"
                    >
                      Current Account Password
                    </label>
                    <div className="relative">
                      <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-slate-400">
                        <Lock className="w-4 h-4" />
                      </div>
                      <input
                        id="enroll-current-password"
                        data-testid="mfa-current-password-input"
                        type={showEnrollPassword ? 'text' : 'password'}
                        required
                        value={enrollPassword}
                        onChange={(e) => setEnrollPassword(e.target.value)}
                        placeholder="••••••••"
                        disabled={enrollSubmitting}
                        className="w-full pl-9 pr-10 py-2 text-sm bg-slate-50 border border-slate-300 rounded-lg text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-sky-500 focus:bg-white transition disabled:opacity-50"
                      />
                      <button
                        type="button"
                        onClick={() => setShowEnrollPassword(!showEnrollPassword)}
                        className="absolute inset-y-0 right-0 pr-3 flex items-center text-slate-400 hover:text-slate-600 focus:outline-none"
                      >
                        {showEnrollPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                      </button>
                    </div>
                  </div>

                  <div>
                    <label
                      htmlFor="enroll-code"
                      className="block text-xs font-semibold text-slate-700 uppercase tracking-wider mb-1"
                    >
                      6-Digit Authenticator Code
                    </label>
                    <input
                      id="enroll-code"
                      data-testid="mfa-enroll-code-input"
                      type="text"
                      maxLength={8}
                      required
                      value={enrollCode}
                      onChange={(e) => setEnrollCode(e.target.value)}
                      placeholder="000000"
                      disabled={enrollSubmitting}
                      className="w-full text-center tracking-widest py-2 text-base font-mono font-bold bg-slate-50 border border-slate-300 rounded-lg text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-sky-500 focus:bg-white transition disabled:opacity-50"
                    />
                  </div>

                  <div className="flex flex-col-reverse sm:flex-row items-center justify-end gap-2.5 pt-3 border-t border-slate-100">
                    <button
                      type="button"
                      data-testid="cancel-enable-2fa-btn"
                      disabled={enrollSubmitting}
                      onClick={() => setShowEnrollModal(false)}
                      className="w-full sm:w-auto px-4 py-2 text-xs font-semibold text-slate-700 hover:text-slate-900 bg-slate-100 hover:bg-slate-200 rounded-lg transition disabled:opacity-50 cursor-pointer"
                    >
                      Cancel
                    </button>
                    <button
                      type="submit"
                      data-testid="submit-enable-2fa-btn"
                      disabled={enrollSubmitting}
                      className="w-full sm:w-auto inline-flex items-center justify-center space-x-1.5 px-4 py-2 text-xs font-semibold text-white bg-sky-600 hover:bg-sky-700 rounded-lg shadow-sm transition disabled:opacity-50 cursor-pointer"
                    >
                      {enrollSubmitting ? (
                        <>
                          <Loader2 className="w-3.5 h-3.5 animate-spin" />
                          <span>Verifying & enabling...</span>
                        </>
                      ) : (
                        <span>Verify & Enable MFA</span>
                      )}
                    </button>
                  </div>
                </form>
              </div>
            ) : null}
          </div>
        </div>
      )}

      {/* Recovery Codes Display Modal (Displayed once) */}
      {displayRecoveryCodes && displayRecoveryCodes.length > 0 && (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="recovery-codes-dialog-title"
          className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/40 backdrop-blur-xs animate-in fade-in"
        >
          <div className="bg-white border border-slate-200 rounded-2xl p-6 sm:p-8 max-w-lg w-full shadow-xl space-y-4 max-h-[90vh] overflow-y-auto">
            <div className="flex items-start space-x-3.5">
              <div className="w-10 h-10 rounded-xl bg-amber-100 text-amber-700 flex items-center justify-center shrink-0">
                <KeyRound className="w-5 h-5 text-amber-600" />
              </div>
              <div className="space-y-1">
                <h3 id="recovery-codes-dialog-title" className="text-base font-bold text-slate-900">
                  Save Your Recovery Codes
                </h3>
                <p className="text-xs text-slate-600 leading-relaxed">
                  Store these single-use recovery codes in a safe place (such as a password manager).
                  If you lose your authenticator device, they are your only way to regain access.
                </p>
              </div>
            </div>

            <div className="p-3 bg-amber-50 border border-amber-200 rounded-xl text-xs text-amber-900 font-medium">
              Important: These codes will NOT be displayed again once you close this window.
            </div>

            {/* Recovery Codes Grid */}
            <div
              data-testid="recovery-codes-list"
              className="grid grid-cols-2 gap-2 p-4 bg-slate-50 border border-slate-200 rounded-xl font-mono text-xs text-slate-800"
            >
              {displayRecoveryCodes.map((code, index) => (
                <div key={index} className="p-1.5 bg-white rounded border border-slate-200 text-center font-bold">
                  {code}
                </div>
              ))}
            </div>

            <div className="flex flex-col-reverse sm:flex-row items-center justify-between gap-2.5 pt-3 border-t border-slate-100">
              <button
                type="button"
                data-testid="copy-recovery-codes-btn"
                onClick={handleCopyRecoveryCodes}
                className="w-full sm:w-auto inline-flex items-center justify-center space-x-1 px-4 py-2 text-xs font-semibold text-slate-700 bg-slate-100 hover:bg-slate-200 border border-slate-200 rounded-lg transition cursor-pointer"
              >
                {copiedRecoveryCodes ? (
                  <>
                    <Check className="w-3.5 h-3.5 text-emerald-600" />
                    <span className="text-emerald-700">Copied to Clipboard</span>
                  </>
                ) : (
                  <>
                    <Copy className="w-3.5 h-3.5 text-slate-500" />
                    <span>Copy All Codes</span>
                  </>
                )}
              </button>

              <button
                type="button"
                data-testid="confirm-saved-recovery-codes-btn"
                onClick={() => {
                  setDisplayRecoveryCodes(null);
                  setCopiedRecoveryCodes(false);
                }}
                className="w-full sm:w-auto px-4 py-2 text-xs font-semibold text-white bg-sky-600 hover:bg-sky-700 rounded-lg shadow-sm transition cursor-pointer"
              >
                I Have Saved These Codes
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Regenerate Recovery Codes Modal */}
      {showRegenModal && (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="regen-dialog-title"
          className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/40 backdrop-blur-xs animate-in fade-in"
        >
          <div className="bg-white border border-slate-200 rounded-2xl p-6 sm:p-8 max-w-md w-full shadow-xl space-y-4">
            <div className="flex items-start space-x-3.5">
              <div className="w-10 h-10 rounded-xl bg-amber-100 text-amber-700 flex items-center justify-center shrink-0">
                <RefreshCw className="w-5 h-5 text-amber-600" />
              </div>
              <div className="space-y-1">
                <h3 id="regen-dialog-title" className="text-base font-bold text-slate-900">
                  Regenerate Recovery Codes?
                </h3>
                <p className="text-xs text-slate-600 leading-relaxed">
                  Generating new recovery codes will invalidate all your previously issued codes.
                </p>
              </div>
            </div>

            {regenError && (
              <div
                role="alert"
                data-testid="regen-error-banner"
                className="p-3 rounded-lg bg-rose-50 border border-rose-200 text-rose-800 text-xs flex items-start space-x-2"
              >
                <AlertCircle className="w-4 h-4 text-rose-600 shrink-0 mt-0.5" />
                <div className="flex-1 font-medium">{regenError}</div>
              </div>
            )}

            <form onSubmit={handleRegenSubmit} className="space-y-3.5">
              <div>
                <label
                  htmlFor="regen-password"
                  className="block text-xs font-semibold text-slate-700 uppercase tracking-wider mb-1"
                >
                  Current Password
                </label>
                <div className="relative">
                  <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-slate-400">
                    <Lock className="w-4 h-4" />
                  </div>
                  <input
                    id="regen-password"
                    data-testid="regen-current-password-input"
                    type={showRegenPassword ? 'text' : 'password'}
                    required
                    value={regenPassword}
                    onChange={(e) => setRegenPassword(e.target.value)}
                    placeholder="••••••••"
                    disabled={regenSubmitting}
                    className="w-full pl-9 pr-10 py-2 text-sm bg-slate-50 border border-slate-300 rounded-lg text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-sky-500 focus:bg-white transition disabled:opacity-50"
                  />
                  <button
                    type="button"
                    onClick={() => setShowRegenPassword(!showRegenPassword)}
                    className="absolute inset-y-0 right-0 pr-3 flex items-center text-slate-400 hover:text-slate-600 focus:outline-none"
                  >
                    {showRegenPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                  </button>
                </div>
              </div>

              <div>
                <label
                  htmlFor="regen-code"
                  className="block text-xs font-semibold text-slate-700 uppercase tracking-wider mb-1"
                >
                  Authenticator Code or Recovery Code
                </label>
                <input
                  id="regen-code"
                  data-testid="regen-code-input"
                  type="text"
                  required
                  value={regenCode}
                  onChange={(e) => setRegenCode(e.target.value)}
                  placeholder="000000 or 1a2b3-4c5d6"
                  disabled={regenSubmitting}
                  className="w-full py-2 px-3 text-sm font-mono bg-slate-50 border border-slate-300 rounded-lg text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-sky-500 focus:bg-white transition disabled:opacity-50"
                />
              </div>

              <div className="flex flex-col-reverse sm:flex-row items-center justify-end gap-2.5 pt-3 border-t border-slate-100">
                <button
                  type="button"
                  data-testid="cancel-regen-codes-btn"
                  disabled={regenSubmitting}
                  onClick={() => setShowRegenModal(false)}
                  className="w-full sm:w-auto px-4 py-2 text-xs font-semibold text-slate-700 hover:text-slate-900 bg-slate-100 hover:bg-slate-200 rounded-lg transition disabled:opacity-50 cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  data-testid="submit-regen-codes-btn"
                  disabled={regenSubmitting}
                  className="w-full sm:w-auto inline-flex items-center justify-center space-x-1.5 px-4 py-2 text-xs font-semibold text-white bg-amber-600 hover:bg-amber-700 rounded-lg shadow-sm transition disabled:opacity-50 cursor-pointer"
                >
                  {regenSubmitting ? (
                    <>
                      <Loader2 className="w-3.5 h-3.5 animate-spin" />
                      <span>Regenerating...</span>
                    </>
                  ) : (
                    <span>Regenerate Codes</span>
                  )}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Disable 2FA Modal */}
      {showDisableModal && (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="disable-dialog-title"
          className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/40 backdrop-blur-xs animate-in fade-in"
        >
          <div className="bg-white border border-slate-200 rounded-2xl p-6 sm:p-8 max-w-md w-full shadow-xl space-y-4">
            <div className="flex items-start space-x-3.5">
              <div className="w-10 h-10 rounded-xl bg-rose-100 text-rose-700 flex items-center justify-center shrink-0">
                <AlertCircle className="w-5 h-5 text-rose-600" />
              </div>
              <div className="space-y-1">
                <h3 id="disable-dialog-title" className="text-base font-bold text-slate-900">
                  Disable Two-Factor Authentication?
                </h3>
                <p className="text-xs text-slate-600 leading-relaxed">
                  Your account will no longer be protected by a second factor. Anyone with your password will be able to sign in.
                </p>
              </div>
            </div>

            {disableError && (
              <div
                role="alert"
                data-testid="disable-error-banner"
                className="p-3 rounded-lg bg-rose-50 border border-rose-200 text-rose-800 text-xs flex items-start space-x-2"
              >
                <AlertCircle className="w-4 h-4 text-rose-600 shrink-0 mt-0.5" />
                <div className="flex-1 font-medium">{disableError}</div>
              </div>
            )}

            <form onSubmit={handleDisableSubmit} className="space-y-3.5">
              <div>
                <label
                  htmlFor="disable-password"
                  className="block text-xs font-semibold text-slate-700 uppercase tracking-wider mb-1"
                >
                  Current Password
                </label>
                <div className="relative">
                  <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-slate-400">
                    <Lock className="w-4 h-4" />
                  </div>
                  <input
                    id="disable-password"
                    data-testid="disable-current-password-input"
                    type={showDisablePassword ? 'text' : 'password'}
                    required
                    value={disablePassword}
                    onChange={(e) => setDisablePassword(e.target.value)}
                    placeholder="••••••••"
                    disabled={disableSubmitting}
                    className="w-full pl-9 pr-10 py-2 text-sm bg-slate-50 border border-slate-300 rounded-lg text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-sky-500 focus:bg-white transition disabled:opacity-50"
                  />
                  <button
                    type="button"
                    onClick={() => setShowDisablePassword(!showDisablePassword)}
                    className="absolute inset-y-0 right-0 pr-3 flex items-center text-slate-400 hover:text-slate-600 focus:outline-none"
                  >
                    {showDisablePassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                  </button>
                </div>
              </div>

              <div>
                <label
                  htmlFor="disable-code"
                  className="block text-xs font-semibold text-slate-700 uppercase tracking-wider mb-1"
                >
                  Authenticator Code or Recovery Code
                </label>
                <input
                  id="disable-code"
                  data-testid="disable-code-input"
                  type="text"
                  required
                  value={disableCode}
                  onChange={(e) => setDisableCode(e.target.value)}
                  placeholder="000000 or 1a2b3-4c5d6"
                  disabled={disableSubmitting}
                  className="w-full py-2 px-3 text-sm font-mono bg-slate-50 border border-slate-300 rounded-lg text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-sky-500 focus:bg-white transition disabled:opacity-50"
                />
              </div>

              <div className="flex flex-col-reverse sm:flex-row items-center justify-end gap-2.5 pt-3 border-t border-slate-100">
                <button
                  type="button"
                  data-testid="cancel-disable-2fa-btn"
                  disabled={disableSubmitting}
                  onClick={() => setShowDisableModal(false)}
                  className="w-full sm:w-auto px-4 py-2 text-xs font-semibold text-slate-700 hover:text-slate-900 bg-slate-100 hover:bg-slate-200 rounded-lg transition disabled:opacity-50 cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  data-testid="submit-disable-2fa-btn"
                  disabled={disableSubmitting}
                  className="w-full sm:w-auto inline-flex items-center justify-center space-x-1.5 px-4 py-2 text-xs font-semibold text-white bg-rose-600 hover:bg-rose-700 rounded-lg shadow-sm transition disabled:opacity-50 cursor-pointer"
                >
                  {disableSubmitting ? (
                    <>
                      <Loader2 className="w-3.5 h-3.5 animate-spin" />
                      <span>Disabling...</span>
                    </>
                  ) : (
                    <span>Disable 2FA</span>
                  )}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
