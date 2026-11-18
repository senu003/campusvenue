import React, { useState, useEffect, useCallback } from 'react';
import {
  Server,
  Database,
  CheckCircle2,
  AlertCircle,
  RefreshCw,
  Clock,
  Layers,
  Calendar,
  Building2,
  Users,
  ShieldCheck,
  FileCode2,
  LogIn,
  LogOut,
  UserCheck,
  Inbox,
  UserPlus,
} from 'lucide-react';
import type { HealthStatusResponse } from './types/api';
import { AuthProvider } from './features/auth/AuthContext';
import { useAuth } from './features/auth/useAuth';
import { LoginForm } from './features/auth/LoginForm';
import { AccountScreen } from './features/auth/AccountScreen';
import { RegisterForm } from './features/auth/RegisterForm';
import { ConfirmationNotice } from './features/auth/ConfirmationNotice';
import { EmailVerificationScreen } from './features/auth/EmailVerificationScreen';
import { ForgotPasswordForm } from './features/auth/ForgotPasswordForm';
import { ResetPasswordForm } from './features/auth/ResetPasswordForm';
import { DevMailbox } from './features/auth/DevMailbox';

type ActiveTab = 'overview' | 'auth' | 'mailbox';
type AuthSubView = 'login' | 'register' | 'confirmation-notice' | 'verify' | 'forgot-password' | 'reset-password';

function parseInitialAuthParams(): {
  activeTab: ActiveTab;
  authSubView: AuthSubView;
  verifyUserId: string;
  verifyToken: string;
  resetUserId: string;
  resetToken: string;
} {
  if (typeof window === 'undefined') {
    return {
      activeTab: 'overview',
      authSubView: 'login',
      verifyUserId: '',
      verifyToken: '',
      resetUserId: '',
      resetToken: '',
    };
  }

  const searchParams = new URLSearchParams(window.location.search);
  const hashRaw = window.location.hash.startsWith('#')
    ? window.location.hash.slice(1)
    : window.location.hash;
  const hashString = hashRaw.includes('?') ? hashRaw.slice(hashRaw.indexOf('?') + 1) : hashRaw;
  const hashParams = new URLSearchParams(hashString);

  const tab = searchParams.get('tab') || hashParams.get('tab');

  // Fragment token/userId take precedence for reset password (keeping token out of initial HTTP request)
  const fragmentToken = hashParams.get('token') ?? '';
  const fragmentUserId = hashParams.get('userId') ?? '';
  const searchToken = searchParams.get('token') ?? '';
  const searchUserId = searchParams.get('userId') ?? '';

  const token = fragmentToken || searchToken;
  const userId = fragmentUserId || searchUserId;

  if (tab === 'mailbox') {
    return {
      activeTab: 'mailbox',
      authSubView: 'login',
      verifyUserId: '',
      verifyToken: '',
      resetUserId: '',
      resetToken: '',
    };
  }

  if (tab === 'reset-password' || (token && userId && (tab === 'reset-password' || hashRaw.includes('reset')))) {
    return {
      activeTab: 'auth',
      authSubView: 'reset-password',
      verifyUserId: '',
      verifyToken: '',
      resetUserId: userId,
      resetToken: token,
    };
  }

  if (tab === 'forgot-password') {
    return {
      activeTab: 'auth',
      authSubView: 'forgot-password',
      verifyUserId: '',
      verifyToken: '',
      resetUserId: '',
      resetToken: '',
    };
  }

  if (tab === 'verify' || (token && userId)) {
    return {
      activeTab: 'auth',
      authSubView: 'verify',
      verifyUserId: userId,
      verifyToken: token,
      resetUserId: '',
      resetToken: '',
    };
  }

  if (tab === 'register') {
    return {
      activeTab: 'auth',
      authSubView: 'register',
      verifyUserId: '',
      verifyToken: '',
      resetUserId: '',
      resetToken: '',
    };
  }

  if (tab === 'auth') {
    return {
      activeTab: 'auth',
      authSubView: 'login',
      verifyUserId: '',
      verifyToken: '',
      resetUserId: '',
      resetToken: '',
    };
  }

  return {
    activeTab: 'overview',
    authSubView: 'login',
    verifyUserId: '',
    verifyToken: '',
    resetUserId: '',
    resetToken: '',
  };
}

const CampusVenueContent: React.FC = () => {
  const { user, isAuthenticated, logout, isLoading: authLoading } = useAuth();
  const [initialAuth] = useState(parseInitialAuthParams);

  const [verifyParams, setVerifyParams] = useState<{ userId: string; token: string }>({
    userId: initialAuth.verifyUserId,
    token: initialAuth.verifyToken,
  });

  const [resetParams, setResetParams] = useState<{ userId: string; token: string }>({
    userId: initialAuth.resetUserId,
    token: initialAuth.resetToken,
  });

  const [activeTab, setActiveTab] = useState<ActiveTab>(initialAuth.activeTab);
  const [authSubView, setAuthSubView] = useState<AuthSubView>(initialAuth.authSubView);

  const [registeredEmail, setRegisteredEmail] = useState<string>('');
  const [loginSuccessMessage, setLoginSuccessMessage] = useState<string | null>(null);

  const [health, setHealth] = useState<HealthStatusResponse | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [lastChecked, setLastChecked] = useState<Date | null>(null);

  const fetchHealthStatus = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch('/health', {
        headers: {
          'Accept': 'application/json',
        },
      });

      if (!response.ok && response.status !== 503) {
        throw new Error(`Server returned HTTP ${response.status} (${response.statusText})`);
      }

      const data: HealthStatusResponse = await response.json();
      setHealth(data);
      setLastChecked(new Date());
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to connect to backend API';
      setError(message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    let ignore = false;

    async function initialCheck() {
      try {
        const response = await fetch('/health', {
          headers: {
            'Accept': 'application/json',
          },
        });

        if (!response.ok && response.status !== 503) {
          throw new Error(`Server returned HTTP ${response.status} (${response.statusText})`);
        }

        const data: HealthStatusResponse = await response.json();
        if (!ignore) {
          setHealth(data);
          setLastChecked(new Date());
        }
      } catch (err: unknown) {
        if (!ignore) {
          const message = err instanceof Error ? err.message : 'Failed to connect to backend API';
          setError(message);
        }
      } finally {
        if (!ignore) {
          setLoading(false);
        }
      }
    }

    initialCheck();

    return () => {
      ignore = true;
    };
  }, []);

  const modules = [
    { name: 'Identity & Access', phase: 'Phase 1A', icon: ShieldCheck, desc: 'Authentication, Self-Service Sign-up, Email Verification, Password Recovery, Same-Origin HttpOnly cookies, CSRF defense.' },
    { name: 'Organizations', phase: 'Phase 1C', icon: Users, desc: 'University departments, student societies, administrative units.' },
    { name: 'Facilities & Spaces', phase: 'Phase 2', icon: Building2, desc: 'Auditoriums, seminar halls, capacities, AV equipment.' },
    { name: 'Reservations', phase: 'Phases 3-4', icon: Calendar, desc: 'Timetable conflict detection, setup/cleanup buffers, approvals.' },
    { name: 'Events & Sessions', phase: 'Phase 6', icon: Layers, desc: 'Multi-session academic and cultural event publishing.' },
    { name: '2D Seating & Holds', phase: 'Phases 8-9', icon: Layers, desc: 'Interactive vector layouts, ACID concurrent-safe seat locks.' },
  ];

  return (
    <div className="min-h-screen bg-slate-50 flex flex-col font-sans">
      {/* Header */}
      <header className="bg-white border-b border-slate-200 sticky top-0 z-10 shadow-sm">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-16 flex items-center justify-between">
          <div className="flex items-center space-x-3">
            <button
              onClick={() => setActiveTab('overview')}
              className="flex items-center space-x-3 text-left focus:outline-none cursor-pointer"
            >
              <div className="w-10 h-10 rounded-lg bg-sky-600 flex items-center justify-center text-white font-bold text-xl shadow-sm">
                CV
              </div>
              <div>
                <div className="flex items-center space-x-2">
                  <span className="text-xl font-bold text-slate-900 tracking-tight">CampusVenue</span>
                  <span className="px-2 py-0.5 text-xs font-semibold bg-sky-100 text-sky-800 rounded-full border border-sky-200">
                    Phase 1A Active
                  </span>
                </div>
                <p className="text-xs text-slate-500 hidden sm:block">University Venue Reservation & Event Management</p>
              </div>
            </button>
          </div>

          {/* Navigation Controls */}
          <div className="flex items-center space-x-3">
            <nav className="flex items-center space-x-1 bg-slate-100 p-1 rounded-lg border border-slate-200">
              <button
                data-testid="nav-overview-tab"
                onClick={() => setActiveTab('overview')}
                className={`px-3 py-1.5 text-xs font-medium rounded-md transition cursor-pointer ${activeTab === 'overview'
                  ? 'bg-white text-slate-900 shadow-xs font-semibold'
                  : 'text-slate-600 hover:text-slate-900'
                  }`}
              >
                System Health
              </button>
              <button
                data-testid="nav-auth-tab"
                onClick={() => {
                  setActiveTab('auth');
                  if (!isAuthenticated && authSubView !== 'verify') {
                    setAuthSubView('login');
                  }
                }}
                className={`px-3 py-1.5 text-xs font-medium rounded-md transition cursor-pointer ${activeTab === 'auth'
                  ? 'bg-white text-sky-700 shadow-xs font-semibold'
                  : 'text-slate-600 hover:text-slate-900'
                  }`}
              >
                {isAuthenticated ? 'My Account' : 'Sign In'}
              </button>
              <button
                data-testid="nav-mailbox-tab"
                onClick={() => setActiveTab('mailbox')}
                className={`px-3 py-1.5 text-xs font-medium rounded-md transition cursor-pointer flex items-center space-x-1.5 ${activeTab === 'mailbox'
                  ? 'bg-white text-sky-700 shadow-xs font-semibold'
                  : 'text-slate-600 hover:text-slate-900'
                  }`}
              >
                <Inbox className="w-3.5 h-3.5" />
                <span>Dev Mailbox</span>
              </button>
            </nav>

            <a
              href="/swagger"
              target="_blank"
              rel="noreferrer"
              className="hidden sm:inline-flex items-center space-x-1.5 px-3 py-1.5 text-xs font-medium text-slate-700 bg-slate-100 hover:bg-slate-200 rounded-md transition"
            >
              <FileCode2 className="w-4 h-4 text-slate-500" />
              <span>Swagger API</span>
            </a>

            {isAuthenticated ? (
              <div className="flex items-center space-x-2">
                <span className="hidden md:inline-flex items-center space-x-1 text-xs text-slate-700 font-medium px-2.5 py-1 bg-slate-100 rounded-md border border-slate-200">
                  <UserCheck className="w-3.5 h-3.5 text-emerald-600" />
                  <span>{user?.email}</span>
                </span>
                <button
                  onClick={logout}
                  disabled={authLoading}
                  className="inline-flex items-center space-x-1 px-3 py-1.5 text-xs font-medium text-rose-700 bg-rose-50 hover:bg-rose-100 rounded-md border border-rose-200 transition disabled:opacity-50 cursor-pointer"
                >
                  <LogOut className="w-3.5 h-3.5" />
                  <span>Sign Out</span>
                </button>
              </div>
            ) : (
              <button
                onClick={() => {
                  setActiveTab('auth');
                  setAuthSubView('login');
                }}
                className="inline-flex items-center space-x-1.5 px-3 py-1.5 text-xs font-semibold text-white bg-sky-600 hover:bg-sky-700 rounded-md shadow-sm transition cursor-pointer"
              >
                <LogIn className="w-3.5 h-3.5" />
                <span>Sign In</span>
              </button>
            )}
          </div>
        </div>
      </header>

      {/* Main Content */}
      <main className="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-8">
        {activeTab === 'mailbox' ? (
          <section aria-label="Development Mailbox Portal">
            <DevMailbox
              onSelectVerificationLink={(userId, token) => {
                setVerifyParams({ userId, token });
                setAuthSubView('verify');
                setActiveTab('auth');
              }}
              onSelectResetLink={(userId, token) => {
                setResetParams({ userId, token });
                setAuthSubView('reset-password');
                setActiveTab('auth');
              }}
            />
          </section>
        ) : activeTab === 'auth' ? (
          <section aria-label="Authentication Portal">
            {isAuthenticated ? (
              <AccountScreen
                onPasswordChanged={(msg) => {
                  setLoginSuccessMessage(msg);
                  setAuthSubView('login');
                  setActiveTab('auth');
                }}
              />
            ) : authSubView === 'verify' ? (
              <EmailVerificationScreen
                userId={verifyParams.userId}
                token={verifyParams.token}
                onNavigateToLogin={() => setAuthSubView('login')}
              />
            ) : authSubView === 'confirmation-notice' ? (
              <ConfirmationNotice
                email={registeredEmail}
                onNavigateToLogin={() => setAuthSubView('login')}
                onNavigateToMailbox={() => setActiveTab('mailbox')}
              />
            ) : authSubView === 'register' ? (
              <RegisterForm
                onSuccess={(email) => {
                  setRegisteredEmail(email);
                  setAuthSubView('confirmation-notice');
                }}
                onNavigateToLogin={() => setAuthSubView('login')}
              />
            ) : authSubView === 'forgot-password' ? (
              <ForgotPasswordForm
                initialEmail={registeredEmail}
                onNavigateToLogin={() => setAuthSubView('login')}
                onNavigateToMailbox={() => setActiveTab('mailbox')}
              />
            ) : authSubView === 'reset-password' ? (
              <ResetPasswordForm
                userId={resetParams.userId}
                token={resetParams.token}
                onSuccess={(msg) => {
                  setLoginSuccessMessage(msg);
                  setAuthSubView('login');
                }}
                onNavigateToLogin={() => setAuthSubView('login')}
                onRequestNewLink={() => setAuthSubView('forgot-password')}
              />
            ) : (
              <LoginForm
                initialEmail={registeredEmail}
                successMessage={loginSuccessMessage}
                onSuccess={() => {
                  setLoginSuccessMessage(null);
                  setActiveTab('auth');
                }}
                onNavigateToRegister={() => {
                  setLoginSuccessMessage(null);
                  setAuthSubView('register');
                }}
                onNavigateToForgotPassword={() => {
                  setLoginSuccessMessage(null);
                  setAuthSubView('forgot-password');
                }}
                onNavigateToMailbox={() => setActiveTab('mailbox')}
              />
            )}
          </section>
        ) : (
          <>
            {/* Hero Banner */}
            <section className="bg-white border border-slate-200 rounded-2xl p-6 sm:p-8 shadow-sm">
              <div className="max-w-3xl space-y-4">
                <div className="flex items-center space-x-2">
                  <span className="px-2.5 py-0.5 text-xs font-bold uppercase tracking-wider bg-emerald-100 text-emerald-800 rounded-md">
                    Phase 1A Active Slice
                  </span>
                  <span className="text-xs text-slate-400">|</span>
                  <span className="text-xs font-semibold text-slate-600">Self-Service Sign-Up, Verification & Password Recovery</span>
                </div>
                <h1 className="text-2xl sm:text-3xl font-extrabold text-slate-900 tracking-tight">
                  University Venue Reservation & Management
                </h1>
                <p className="text-slate-600 leading-relaxed text-sm sm:text-base">
                  CampusVenue provides an auditable, high-reliability platform for managing university auditoriums,
                  lecture halls, scheduling conflict detection, and concurrent-safe event ticketing.
                  Phase 1A delivers self-service registration, password recovery, policy verification, and local email confirmation
                  without needing an external mail account or PowerShell user seeding.
                </p>
                <div className="pt-2 flex flex-wrap gap-3">
                  {isAuthenticated ? (
                    <button
                      onClick={() => setActiveTab('auth')}
                      className="inline-flex items-center space-x-2 px-4 py-2 bg-sky-600 hover:bg-sky-700 text-white rounded-lg text-xs font-bold shadow-sm transition cursor-pointer"
                    >
                      <UserCheck className="w-4 h-4" />
                      <span>Open Account Screen</span>
                    </button>
                  ) : (
                    <>
                      <button
                        data-testid="hero-signup-btn"
                        onClick={() => {
                          setActiveTab('auth');
                          setAuthSubView('register');
                        }}
                        className="inline-flex items-center space-x-2 px-4 py-2 bg-sky-600 hover:bg-sky-700 text-white rounded-lg text-xs font-bold shadow-sm transition cursor-pointer"
                      >
                        <UserPlus className="w-4 h-4" />
                        <span>Create Account</span>
                      </button>
                      <button
                        onClick={() => {
                          setActiveTab('auth');
                          setAuthSubView('login');
                        }}
                        className="inline-flex items-center space-x-2 px-4 py-2 bg-slate-100 hover:bg-slate-200 text-slate-700 rounded-lg text-xs font-bold transition cursor-pointer"
                      >
                        <LogIn className="w-4 h-4" />
                        <span>Sign In</span>
                      </button>
                    </>
                  )}
                  <button
                    data-testid="hero-mailbox-btn"
                    onClick={() => setActiveTab('mailbox')}
                    className="inline-flex items-center space-x-2 px-4 py-2 bg-slate-100 hover:bg-slate-200 text-slate-700 rounded-lg text-xs font-bold transition cursor-pointer"
                  >
                    <Inbox className="w-3.5 h-3.5 text-sky-600" />
                    <span>View Dev Mailbox</span>
                  </button>
                  <button
                    onClick={fetchHealthStatus}
                    disabled={loading}
                    className="inline-flex items-center space-x-2 px-4 py-2 bg-slate-100 hover:bg-slate-200 text-slate-700 rounded-lg text-xs font-bold transition cursor-pointer"
                  >
                    <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
                    <span>Re-check System Health</span>
                  </button>
                </div>
              </div>
            </section>

            {/* System Status Dashboard Card */}
            <section aria-label="System Status" className="bg-white border border-slate-200 rounded-2xl p-6 sm:p-8 shadow-sm space-y-6">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between pb-4 border-b border-slate-100 gap-2">
                <div>
                  <h2 className="text-lg font-bold text-slate-900 flex items-center space-x-2">
                    <Server className="w-5 h-5 text-sky-600" />
                    <span>Backend Health & Connectivity</span>
                  </h2>
                  <p className="text-xs text-slate-500">Live endpoint verification via <code className="bg-slate-100 px-1 py-0.5 rounded text-sky-700 font-mono">/health</code></p>
                </div>
                {lastChecked && (
                  <span className="text-xs text-slate-400 flex items-center space-x-1">
                    <Clock className="w-3.5 h-3.5" />
                    <span>Checked {lastChecked.toLocaleTimeString()}</span>
                  </span>
                )}
              </div>

              {loading ? (
                <div className="flex items-center justify-center py-12 space-x-3 text-slate-500">
                  <RefreshCw className="w-6 h-6 animate-spin text-sky-600" />
                  <span className="text-sm font-medium">Verifying PostgreSQL connection and service health...</span>
                </div>
              ) : error ? (
                <div role="alert" className="p-4 bg-rose-50 border border-rose-200 rounded-xl flex items-start space-x-3 text-rose-800">
                  <AlertCircle className="w-5 h-5 text-rose-600 shrink-0 mt-0.5" />
                  <div className="space-y-1">
                    <h3 className="text-sm font-bold">Health Check Failed</h3>
                    <p className="text-xs leading-relaxed">{error}</p>
                  </div>
                </div>
              ) : health ? (
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  {/* API Service Status */}
                  <div className="p-4 rounded-xl border border-slate-200 bg-slate-50/50 flex items-start justify-between">
                    <div className="space-y-1">
                      <div className="flex items-center space-x-2">
                        <span className="text-xs font-bold uppercase tracking-wider text-slate-500">Service</span>
                        <Server className="w-3.5 h-3.5 text-slate-400" />
                      </div>
                      <p className="text-sm font-bold text-slate-900">CampusVenue.Api</p>
                      <p className="text-xs text-slate-500">Environment: <span className="font-semibold text-slate-700">{health.environment}</span></p>
                      <p className="text-xs text-slate-500">Latency: <span className="font-mono text-slate-700">{health.totalDurationMs.toFixed(1)} ms</span></p>
                    </div>
                    <span className="inline-flex items-center space-x-1 px-2.5 py-1 rounded-full text-xs font-bold bg-emerald-100 text-emerald-800">
                      <CheckCircle2 className="w-3.5 h-3.5 text-emerald-600" />
                      <span>{health.status}</span>
                    </span>
                  </div>

                  {/* PostgreSQL Database Status */}
                  {(() => {
                    const pg = health.checks?.find(c => c.name.toLowerCase().includes('postgre') || c.name.toLowerCase().includes('db'));
                    const isHealthy = pg ? pg.status === 'Healthy' : health.status === 'Healthy';
                    return (
                      <div className="p-4 rounded-xl border border-slate-200 bg-slate-50/50 flex items-start justify-between">
                        <div className="space-y-1">
                          <div className="flex items-center space-x-2">
                            <span className="text-xs font-bold uppercase tracking-wider text-slate-500">Storage</span>
                            <Database className="w-3.5 h-3.5 text-slate-400" />
                          </div>
                          <p className="text-sm font-bold text-slate-900">PostgreSQL</p>
                          <p className="text-xs text-slate-500">
                            Target: <span className="font-mono text-slate-700">campusvenue_dev</span>
                          </p>
                          {pg && <p className="text-xs text-slate-500 font-mono">{pg.durationMs.toFixed(1)} ms</p>}
                        </div>
                        <span className={`inline-flex items-center space-x-1 px-2.5 py-1 rounded-full text-xs font-bold ${isHealthy
                          ? 'bg-emerald-100 text-emerald-800'
                          : 'bg-rose-100 text-rose-800'
                          }`}>
                          {isHealthy ? (
                            <CheckCircle2 className="w-3.5 h-3.5 text-emerald-600" />
                          ) : (
                            <AlertCircle className="w-3.5 h-3.5 text-rose-600" />
                          )}
                          <span>{pg ? pg.status : health.status}</span>
                        </span>
                      </div>
                    );
                  })()}

                  {/* Dev Mailbox Service */}
                  <div className="p-4 rounded-xl border border-slate-200 bg-slate-50/50 flex items-start justify-between">
                    <div className="space-y-1">
                      <div className="flex items-center space-x-2">
                        <span className="text-xs font-bold uppercase tracking-wider text-slate-500">Dev Mailbox</span>
                        <Inbox className="w-3.5 h-3.5 text-slate-400" />
                      </div>
                      <p className="text-sm font-bold text-slate-900">In-Memory Local Inbox</p>
                      <p className="text-xs text-slate-500">Endpoint: <span className="font-mono text-slate-700">/dev/mailbox</span></p>
                      <button
                        onClick={() => setActiveTab('mailbox')}
                        className="text-xs text-sky-600 hover:text-sky-800 underline font-medium cursor-pointer"
                      >
                        Open In-App Mailbox &rarr;
                      </button>
                    </div>
                    <span className="inline-flex items-center space-x-1 px-2.5 py-1 rounded-full text-xs font-bold bg-sky-100 text-sky-800">
                      <CheckCircle2 className="w-3.5 h-3.5 text-sky-600" />
                      <span>Active</span>
                    </span>
                  </div>
                </div>
              ) : null}
            </section>

            {/* Modular Architecture Roadmap Cards */}
            <section aria-label="Architecture Roadmap" className="space-y-4">
              <div>
                <h2 className="text-lg font-bold text-slate-900">Modular Monolith Architecture Roadmap</h2>
                <p className="text-xs text-slate-500">High-cohesion business modules delivered through vertical slices</p>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                {modules.map((mod) => {
                  const Icon = mod.icon;
                  return (
                    <div
                      key={mod.name}
                      className="bg-white border border-slate-200 rounded-xl p-5 shadow-2xs hover:shadow-xs transition space-y-3"
                    >
                      <div className="flex items-center justify-between">
                        <div className="w-8 h-8 rounded-lg bg-sky-50 text-sky-600 flex items-center justify-center">
                          <Icon className="w-4 h-4" />
                        </div>
                        <span className={`text-2xs font-bold uppercase px-2 py-0.5 rounded-full ${mod.phase === 'Phase 1A.2'
                          ? 'bg-emerald-100 text-emerald-800'
                          : 'bg-slate-100 text-slate-600'
                          }`}>
                          {mod.phase}
                        </span>
                      </div>
                      <div>
                        <h3 className="text-sm font-bold text-slate-900">{mod.name}</h3>
                        <p className="text-xs text-slate-500 leading-relaxed mt-1">{mod.desc}</p>
                      </div>
                    </div>
                  );
                })}
              </div>
            </section>
          </>
        )}
      </main>

      {/* Footer */}
      <footer className="bg-white border-t border-slate-200 py-6">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 flex flex-col sm:flex-row items-center justify-between text-xs text-slate-500 gap-2">
          <p>&copy; {new Date().getFullYear()} CampusVenue &bull; University Venue Reservation & Event Management</p>
          <div className="flex items-center space-x-4">
            <span>ASP.NET Core 10 LTS</span>
            <span>&bull;</span>
            <span>React + TypeScript</span>
            <span>&bull;</span>
            <span>PostgreSQL 16</span>
          </div>
        </div>
      </footer>
    </div>
  );
};

interface ErrorBoundaryProps {
  children: React.ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
}

class ErrorBoundary extends React.Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: React.ErrorInfo) {
    console.error('CampusVenue ErrorBoundary caught error:', error, errorInfo);
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="min-h-screen bg-slate-50 flex items-center justify-center p-4">
          <div className="max-w-md w-full bg-white border border-rose-200 rounded-2xl p-6 shadow-sm space-y-4">
            <div className="flex items-center space-x-3 text-rose-600">
              <AlertCircle className="w-6 h-6 shrink-0" />
              <h2 className="text-lg font-bold text-slate-900">Application Error</h2>
            </div>
            <p className="text-xs text-slate-600 leading-relaxed font-mono bg-slate-50 p-3 rounded-lg border border-slate-200">
              {this.state.error?.message ?? 'An unexpected render error occurred.'}
            </p>
            <button
              onClick={() => {
                this.setState({ hasError: false, error: null });
                window.location.href = '/';
              }}
              className="w-full py-2 px-4 bg-sky-600 hover:bg-sky-700 text-white rounded-lg text-xs font-bold transition cursor-pointer"
            >
              Reload Application
            </button>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}

export const App: React.FC = () => {
  return (
    <ErrorBoundary>
      <AuthProvider>
        <CampusVenueContent />
      </AuthProvider>
    </ErrorBoundary>
  );
};

export default App;
