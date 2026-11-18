import React, { useState } from 'react';
import { ShieldCheck, LogOut, RefreshCw, KeyRound, CheckCircle2, Clock } from 'lucide-react';
import { useAuth } from './useAuth';

export const AccountScreen: React.FC = () => {
  const { user, logout, checkSession, isLoading } = useAuth();
  const [refreshing, setRefreshing] = useState(false);
  const [lastVerified, setLastVerified] = useState<Date>(new Date());

  if (!user) {
    return null;
  }

  const handleRefresh = async () => {
    setRefreshing(true);
    await checkSession();
    setLastVerified(new Date());
    setRefreshing(false);
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
    </div>
  );
};
