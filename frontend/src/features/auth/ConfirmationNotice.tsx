import React from 'react';
import { MailCheck, ExternalLink, ArrowRight, Inbox } from 'lucide-react';

interface ConfirmationNoticeProps {
  email: string;
  onNavigateToLogin: () => void;
  onNavigateToMailbox: () => void;
}

export const ConfirmationNotice: React.FC<ConfirmationNoticeProps> = ({
  email,
  onNavigateToLogin,
  onNavigateToMailbox,
}) => {
  return (
    <div className="w-full max-w-lg mx-auto">
      <div className="bg-white border border-slate-200 rounded-2xl shadow-sm p-6 sm:p-8 space-y-6">
        <div className="text-center space-y-3">
          <div className="inline-flex items-center justify-center w-14 h-14 rounded-2xl bg-emerald-50 text-emerald-600 border border-emerald-100 shadow-2xs">
            <MailCheck className="w-8 h-8" />
          </div>
          <h2 className="text-2xl font-bold text-slate-900 tracking-tight">Check your email</h2>
          <p className="text-sm text-slate-600 max-w-sm mx-auto">
            We've sent a verification link to <strong className="text-slate-900">{email}</strong>. Please confirm your email address before signing in.
          </p>
        </div>

        {/* Local Development Mailbox Card */}
        <div className="bg-sky-50 border border-sky-200 rounded-xl p-4 sm:p-5 space-y-3">
          <div className="flex items-center space-x-2 text-sky-900">
            <Inbox className="w-5 h-5 text-sky-600" />
            <span className="font-bold text-sm">Local Development Inbox</span>
            <span className="px-2 py-0.5 text-2xs font-bold uppercase tracking-wider bg-sky-200 text-sky-800 rounded-full">
              Dev Mode
            </span>
          </div>
          <p className="text-xs text-sky-800 leading-relaxed">
            No external email delivery is needed in development. Your verification email has been captured locally.
            You can inspect it in the built-in mailbox or standalone web view.
          </p>
          <div className="flex flex-wrap gap-2 pt-1">
            <button
              type="button"
              data-testid="open-dev-mailbox-btn"
              onClick={onNavigateToMailbox}
              className="inline-flex items-center space-x-1.5 px-3 py-1.5 bg-sky-600 hover:bg-sky-700 text-white rounded-md text-xs font-semibold shadow-2xs transition"
            >
              <Inbox className="w-3.5 h-3.5" />
              <span>View in Dev Mailbox Tab</span>
            </button>
            <a
              href="/dev/mailbox"
              target="_blank"
              rel="noreferrer"
              className="inline-flex items-center space-x-1.5 px-3 py-1.5 bg-white hover:bg-slate-50 text-sky-700 border border-sky-300 rounded-md text-xs font-semibold shadow-2xs transition"
            >
              <ExternalLink className="w-3.5 h-3.5" />
              <span>Open Standalone Mailbox (/dev/mailbox)</span>
            </a>
          </div>
        </div>

        {/* Action Buttons */}
        <div className="pt-2 flex flex-col sm:flex-row gap-3">
          <button
            type="button"
            data-testid="proceed-to-signin-btn"
            onClick={onNavigateToLogin}
            className="flex-1 flex items-center justify-center space-x-2 py-2.5 px-4 bg-slate-900 hover:bg-slate-800 text-white text-xs font-bold rounded-lg shadow-sm transition"
          >
            <span>Proceed to Sign In</span>
            <ArrowRight className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>
    </div>
  );
};
