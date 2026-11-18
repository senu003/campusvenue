import React, { useState, useEffect, useCallback } from 'react';
import { Inbox, RefreshCw, Trash2, ExternalLink, Mail, Clock, CheckCircle, AlertCircle } from 'lucide-react';
import type { DevEmailDto } from './types';
import { fetchDevMailbox, clearDevMailbox } from './api';

interface DevMailboxProps {
  onSelectVerificationLink?: (userId: string, token: string) => void;
}

export const DevMailbox: React.FC<DevMailboxProps> = ({ onSelectVerificationLink }) => {
  const [emails, setEmails] = useState<DevEmailDto[]>([]);
  const [selectedEmail, setSelectedEmail] = useState<DevEmailDto | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  const refreshEmails = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await fetchDevMailbox();
      setEmails(data);
      setSelectedEmail((prev) => prev ?? (data.length > 0 ? data[0] : null));
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to fetch mailbox messages';
      setError(msg);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    let ignore = false;

    async function load() {
      try {
        const data = await fetchDevMailbox();
        if (!ignore) {
          setEmails(data);
          setSelectedEmail((prev) => prev ?? (data.length > 0 ? data[0] : null));
        }
      } catch (err: unknown) {
        if (!ignore) {
          const msg = err instanceof Error ? err.message : 'Failed to fetch mailbox messages';
          setError(msg);
        }
      } finally {
        if (!ignore) {
          setLoading(false);
        }
      }
    }

    load();

    return () => {
      ignore = true;
    };
  }, []);

  const handleClear = async () => {
    try {
      await clearDevMailbox();
      setEmails([]);
      setSelectedEmail(null);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to clear mailbox';
      setError(msg);
    }
  };

  // Helper to extract userId and token from email HTML for quick one-click verification in SPA
  const extractVerificationParams = (html: string) => {
    const match = html.match(/userId=([a-f0-9-]+)&(?:amp;)?token=([^"'<>&]+)/);
    if (match) {
      return {
        userId: match[1],
        token: decodeURIComponent(match[2]),
      };
    }
    return null;
  };

  return (
    <div className="w-full max-w-5xl mx-auto space-y-6">
      {/* Top Card Header */}
      <div className="bg-white border border-slate-200 rounded-2xl p-6 shadow-sm flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <div className="flex items-center space-x-2">
            <h2 className="text-xl font-bold text-slate-900 flex items-center space-x-2">
              <Inbox className="w-6 h-6 text-sky-600" />
              <span>Development Mailbox</span>
            </h2>
            <span className="px-2.5 py-0.5 text-xs font-bold uppercase tracking-wider bg-sky-100 text-sky-800 rounded-full border border-sky-200">
              Local Inbox
            </span>
          </div>
          <p className="text-xs text-slate-500 mt-1">
            Intercepts confirmation emails locally. No external mail server, Docker, or paid provider required.
          </p>
        </div>

        <div className="flex items-center space-x-2">
          <button
            type="button"
            onClick={refreshEmails}
            disabled={loading}
            className="inline-flex items-center space-x-1.5 px-3 py-1.5 text-xs font-medium text-slate-700 bg-slate-100 hover:bg-slate-200 rounded-lg transition disabled:opacity-50"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
            <span>Refresh</span>
          </button>
          <button
            type="button"
            onClick={handleClear}
            disabled={emails.length === 0}
            className="inline-flex items-center space-x-1.5 px-3 py-1.5 text-xs font-medium text-rose-700 bg-rose-50 hover:bg-rose-100 border border-rose-200 rounded-lg transition disabled:opacity-50"
          >
            <Trash2 className="w-3.5 h-3.5" />
            <span>Clear Inbox</span>
          </button>
          <a
            href="/dev/mailbox"
            target="_blank"
            rel="noreferrer"
            className="inline-flex items-center space-x-1.5 px-3 py-1.5 text-xs font-semibold text-white bg-sky-600 hover:bg-sky-700 rounded-lg shadow-2xs transition"
          >
            <ExternalLink className="w-3.5 h-3.5" />
            <span>Open Standalone View</span>
          </a>
        </div>
      </div>

      {error && (
        <div className="flex items-center space-x-2 p-3.5 bg-rose-50 border border-rose-200 text-rose-800 rounded-lg text-xs font-medium">
          <AlertCircle className="w-4 h-4 text-rose-600 shrink-0" />
          <span>{error}</span>
        </div>
      )}

      {/* Main Mailbox Grid */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6 items-start">
        {/* Messages List (Left column) */}
        <div className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
          <div className="p-4 border-b border-slate-100 bg-slate-50 flex items-center justify-between">
            <span className="text-xs font-bold text-slate-700 uppercase tracking-wider">Messages ({emails.length})</span>
            {emails.length > 0 && <span className="text-2xs text-slate-400">Latest first</span>}
          </div>

          <div className="divide-y divide-slate-100 max-h-[460px] overflow-y-auto">
            {emails.length === 0 ? (
              <div className="p-8 text-center text-slate-400 space-y-2">
                <Mail className="w-8 h-8 mx-auto text-slate-300" />
                <p className="text-xs font-medium text-slate-600">No emails captured yet</p>
                <p className="text-2xs text-slate-400">Self-register an account to receive a verification email</p>
              </div>
            ) : (
              emails.map((msg) => {
                const isSelected = selectedEmail?.id === msg.id;
                return (
                  <button
                    key={msg.id}
                    type="button"
                    onClick={() => setSelectedEmail(msg)}
                    className={`w-full text-left p-3.5 transition focus:outline-none ${
                      isSelected ? 'bg-sky-50/70 border-l-4 border-sky-600' : 'hover:bg-slate-50'
                    }`}
                  >
                    <div className="flex items-center justify-between text-2xs text-slate-400 mb-1">
                      <span className="font-semibold text-sky-800 truncate max-w-[140px]">{msg.to}</span>
                      <span className="flex items-center space-x-1 shrink-0">
                        <Clock className="w-3 h-3" />
                        <span>{new Date(msg.sentAtUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>
                      </span>
                    </div>
                    <div className="text-xs font-bold text-slate-800 truncate">{msg.subject}</div>
                  </button>
                );
              })
            )}
          </div>
        </div>

        {/* Selected Message Preview (Right column, 2 spans) */}
        <div className="md:col-span-2 bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden min-h-[360px] flex flex-col">
          {selectedEmail ? (
            <div className="p-6 space-y-4 flex-1 flex flex-col">
              <div className="border-b border-slate-100 pb-4 space-y-2">
                <div className="flex items-start justify-between gap-2">
                  <h3 className="text-base font-bold text-slate-900">{selectedEmail.subject}</h3>
                  <span className="text-xs text-slate-400 shrink-0">
                    {new Date(selectedEmail.sentAtUtc).toLocaleString()}
                  </span>
                </div>
                <div className="text-xs text-slate-500">
                  To: <strong className="text-slate-800">{selectedEmail.to}</strong>
                </div>

                {/* Quick One-Click Verification Bar */}
                {extractVerificationParams(selectedEmail.bodyHtml) && onSelectVerificationLink && (
                  <div className="mt-3 p-3 bg-emerald-50 border border-emerald-200 rounded-lg flex items-center justify-between">
                    <div className="flex items-center space-x-2 text-emerald-900 text-xs">
                      <CheckCircle className="w-4 h-4 text-emerald-600 shrink-0" />
                      <span className="font-medium">Verification link detected in this email</span>
                    </div>
                    <button
                      type="button"
                      data-testid="one-click-verify-btn"
                      onClick={() => {
                        const params = extractVerificationParams(selectedEmail.bodyHtml);
                        if (params) {
                          onSelectVerificationLink(params.userId, params.token);
                        }
                      }}
                      className="px-3 py-1 bg-emerald-600 hover:bg-emerald-700 text-white rounded text-xs font-bold shadow-2xs transition"
                    >
                      Confirm Now in App
                    </button>
                  </div>
                )}
              </div>

              {/* Rendered HTML Email Preview */}
              <div
                data-testid="email-body-preview"
                className="flex-1 bg-slate-50 p-4 rounded-xl border border-dashed border-slate-200 overflow-auto"
                dangerouslySetInnerHTML={{ __html: selectedEmail.bodyHtml }}
              />
            </div>
          ) : (
            <div className="flex-1 flex flex-col items-center justify-center p-8 text-center text-slate-400">
              <Inbox className="w-12 h-12 text-slate-300 mb-2" />
              <p className="text-sm font-medium text-slate-600">Select an email to view its content</p>
              <p className="text-xs text-slate-400 mt-1 max-w-xs">
                Incoming verification emails dispatched during local registration will appear here automatically.
              </p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
