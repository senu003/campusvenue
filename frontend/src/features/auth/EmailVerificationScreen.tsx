import React, { useState, useEffect } from 'react';
import { CheckCircle2, AlertCircle, Loader2, ArrowRight } from 'lucide-react';
import { confirmEmail } from './api';

interface EmailVerificationScreenProps {
  userId: string;
  token: string;
  onNavigateToLogin: () => void;
}

export const EmailVerificationScreen: React.FC<EmailVerificationScreenProps> = ({
  userId,
  token,
  onNavigateToLogin,
}) => {
  const hasParams = Boolean(userId && token);
  const [isVerifying, setIsVerifying] = useState<boolean>(hasParams);
  const [isSuccess, setIsSuccess] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(
    hasParams ? null : 'Missing verification parameters. Please use the complete link provided in your email.'
  );

  useEffect(() => {
    if (!userId || !token) return;
    let ignore = false;

    async function performVerification() {
      try {
        await confirmEmail(userId, token);
        if (!ignore) {
          setIsSuccess(true);
        }
      } catch (err: unknown) {
        if (!ignore) {
          const msg = err instanceof Error ? err.message : 'Email confirmation failed. The token may be invalid or expired.';
          setErrorMessage(msg);
          setIsSuccess(false);
        }
      } finally {
        if (!ignore) {
          setIsVerifying(false);
        }
      }
    }

    performVerification();

    return () => {
      ignore = true;
    };
  }, [userId, token]);

  return (
    <div className="w-full max-w-md mx-auto">
      <div className="bg-white border border-slate-200 rounded-2xl shadow-sm p-6 sm:p-8 space-y-6 text-center">
        {isVerifying ? (
          <div className="py-8 space-y-4">
            <Loader2 className="w-10 h-10 text-sky-600 animate-spin mx-auto" />
            <h3 className="text-lg font-bold text-slate-900">Verifying your email address</h3>
            <p className="text-xs text-slate-500 max-w-xs mx-auto">
              Please wait while we validate your confirmation token with the server...
            </p>
          </div>
        ) : isSuccess ? (
          <div className="space-y-4 animate-in fade-in">
            <div className="inline-flex items-center justify-center w-14 h-14 rounded-2xl bg-emerald-50 text-emerald-600 border border-emerald-100 shadow-2xs">
              <CheckCircle2 className="w-8 h-8" />
            </div>
            <h3 className="text-xl font-bold text-slate-900">Email Confirmed!</h3>
            <p className="text-xs text-slate-600 max-w-sm mx-auto leading-relaxed">
              Your university email address has been verified successfully. Your account is now active and ready for use.
            </p>
            <div className="pt-4">
              <button
                type="button"
                data-testid="verified-signin-btn"
                onClick={onNavigateToLogin}
                className="w-full flex items-center justify-center space-x-2 py-2.5 px-4 bg-sky-600 hover:bg-sky-700 text-white text-xs font-bold rounded-lg shadow-sm transition"
              >
                <span>Proceed to Sign In</span>
                <ArrowRight className="w-3.5 h-3.5" />
              </button>
            </div>
          </div>
        ) : (
          <div className="space-y-4 animate-in fade-in">
            <div className="inline-flex items-center justify-center w-14 h-14 rounded-2xl bg-rose-50 text-rose-600 border border-rose-100 shadow-2xs">
              <AlertCircle className="w-8 h-8" />
            </div>
            <h3 className="text-xl font-bold text-slate-900">Confirmation Failed</h3>
            <p className="text-xs text-rose-700 max-w-sm mx-auto leading-relaxed font-medium">
              {errorMessage}
            </p>
            <div className="pt-4">
              <button
                type="button"
                onClick={onNavigateToLogin}
                className="w-full flex items-center justify-center space-x-2 py-2.5 px-4 bg-slate-900 hover:bg-slate-800 text-white text-xs font-bold rounded-lg shadow-sm transition"
              >
                <span>Return to Sign In</span>
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
