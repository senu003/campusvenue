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
  ArrowRight
} from 'lucide-react';
import type { HealthStatusResponse } from './types/api';

export const App: React.FC = () => {
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
    { name: 'Identity & Access', phase: 'Phase 1', icon: ShieldCheck, desc: 'Authentication, RBAC claims, departmental coordinator roles.' },
    { name: 'Organizations', phase: 'Phase 1', icon: Users, desc: 'University departments, student societies, administrative units.' },
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
            <div className="w-10 h-10 rounded-lg bg-sky-600 flex items-center justify-center text-white font-bold text-xl shadow-sm">
              CV
            </div>
            <div>
              <div className="flex items-center space-x-2">
                <span className="text-xl font-bold text-slate-900 tracking-tight">CampusVenue</span>
                <span className="px-2 py-0.5 text-xs font-semibold bg-sky-100 text-sky-800 rounded-full border border-sky-200">
                  Phase 0 Foundation
                </span>
              </div>
              <p className="text-xs text-slate-500 hidden sm:block">University Venue Reservation & Event Management</p>
            </div>
          </div>

          <div className="flex items-center space-x-3">
            <a
              href="/swagger"
              target="_blank"
              rel="noreferrer"
              className="inline-flex items-center space-x-1.5 px-3 py-1.5 text-xs font-medium text-slate-700 bg-slate-100 hover:bg-slate-200 rounded-md transition"
            >
              <FileCode2 className="w-4 h-4 text-slate-500" />
              <span>Swagger API</span>
            </a>
            <button
              onClick={fetchHealthStatus}
              disabled={loading}
              className="inline-flex items-center space-x-1.5 px-3 py-1.5 text-xs font-semibold text-white bg-sky-600 hover:bg-sky-700 disabled:opacity-50 rounded-md shadow-sm transition"
            >
              <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
              <span>{loading ? 'Checking...' : 'Check Health'}</span>
            </button>
          </div>
        </div>
      </header>

      {/* Main Content */}
      <main className="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-8">
        {/* Hero Banner */}
        <section className="bg-white border border-slate-200 rounded-2xl p-6 sm:p-8 shadow-sm">
          <div className="max-w-3xl space-y-4">
            <h1 className="text-2xl sm:text-3xl font-extrabold text-slate-900 tracking-tight">
              Enterprise Project Foundation
            </h1>
            <p className="text-slate-600 leading-relaxed text-sm sm:text-base">
              CampusVenue provides an auditable, high-reliability platform for managing university auditoriums,
              lecture halls, scheduling conflict detection, and concurrent-safe event ticketing.
              Phase 0 verifies environment readiness, permanent engineering rules, and containerized backend connectivity.
            </p>
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
                <span>Last verified: {lastChecked.toLocaleTimeString()}</span>
              </span>
            )}
          </div>

          {/* Asynchronous Loading State */}
          {loading && !health && (
            <div role="status" className="flex flex-col items-center justify-center py-12 space-y-3">
              <RefreshCw className="w-8 h-8 text-sky-600 animate-spin" />
              <p className="text-sm font-medium text-slate-600">Querying backend health endpoint...</p>
            </div>
          )}

          {/* Asynchronous Error State */}
          {error && !loading && (
            <div role="alert" className="bg-amber-50 border border-amber-200 rounded-xl p-5 flex items-start space-x-3 text-amber-900">
              <AlertCircle className="w-5 h-5 text-amber-600 flex-shrink-0 mt-0.5" />
              <div className="space-y-1 text-sm">
                <p className="font-semibold">Unable to establish backend connectivity</p>
                <p className="text-amber-700 text-xs">{error}</p>
                <p className="text-amber-800 text-xs mt-2">
                  Ensure the ASP.NET Core API is running on port 5000 or via Docker Compose.
                </p>
                <button
                  onClick={fetchHealthStatus}
                  className="mt-3 px-3 py-1 bg-amber-600 text-white rounded text-xs font-semibold hover:bg-amber-700 transition"
                >
                  Retry Connection
                </button>
              </div>
            </div>
          )}

          {/* Asynchronous Success State */}
          {health && (
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              {/* Overall Status */}
              <div className="p-4 rounded-xl border border-slate-100 bg-slate-50 space-y-2">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-semibold uppercase tracking-wider text-slate-500">API Status</span>
                  <CheckCircle2 className="w-4 h-4 text-emerald-500" />
                </div>
                <div className="flex items-center space-x-2">
                  <span className="w-2.5 h-2.5 rounded-full bg-emerald-500 animate-pulse"></span>
                  <span className="text-xl font-bold text-slate-800">{health.status}</span>
                </div>
                <p className="text-xs text-slate-500">Latency: {health.totalDurationMs.toFixed(1)} ms</p>
              </div>

              {/* Database Check */}
              <div className="p-4 rounded-xl border border-slate-100 bg-slate-50 space-y-2">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-semibold uppercase tracking-wider text-slate-500">PostgreSQL</span>
                  <Database className="w-4 h-4 text-sky-600" />
                </div>
                {health.checks.find(c => c.name === 'postgresql') ? (
                  <>
                    <div className="flex items-center space-x-2">
                      <span className="w-2.5 h-2.5 rounded-full bg-emerald-500"></span>
                      <span className="text-xl font-bold text-slate-800">
                        {health.checks.find(c => c.name === 'postgresql')?.status}
                      </span>
                    </div>
                    <p className="text-xs text-slate-500">
                      {health.checks.find(c => c.name === 'postgresql')?.description}
                    </p>
                  </>
                ) : (
                  <p className="text-xs text-slate-500">Database check configured</p>
                )}
              </div>

              {/* Environment Info */}
              <div className="p-4 rounded-xl border border-slate-100 bg-slate-50 space-y-2">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-semibold uppercase tracking-wider text-slate-500">Environment</span>
                  <Server className="w-4 h-4 text-slate-500" />
                </div>
                <div className="flex items-center space-x-2">
                  <span className="text-xl font-bold text-slate-800">{health.environment}</span>
                </div>
                <p className="text-xs text-slate-500">
                  {new Date(health.timestamp).toLocaleDateString()} {new Date(health.timestamp).toLocaleTimeString()}
                </p>
              </div>
            </div>
          )}
        </section>

        {/* Modular Monolith Architecture Roadmap */}
        <section aria-label="Architecture Roadmap" className="space-y-4">
          <div className="flex items-center justify-between">
            <h2 className="text-lg font-bold text-slate-900">Modular Monolith Architecture Domains</h2>
            <span className="text-xs font-semibold text-slate-500">Business features strictly begin in Phase 1+</span>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            {modules.map((mod) => {
              const Icon = mod.icon;
              return (
                <div
                  key={mod.name}
                  className="bg-white border border-slate-200 rounded-xl p-5 space-y-3 hover:border-slate-300 transition"
                >
                  <div className="flex items-center justify-between">
                    <div className="w-8 h-8 rounded-lg bg-sky-50 text-sky-700 flex items-center justify-center">
                      <Icon className="w-4 h-4" />
                    </div>
                    <span className="text-xs font-semibold text-slate-400 bg-slate-100 px-2 py-0.5 rounded">
                      {mod.phase}
                    </span>
                  </div>
                  <div>
                    <h3 className="font-bold text-slate-900 text-sm">{mod.name}</h3>
                    <p className="text-xs text-slate-500 mt-1 leading-relaxed">{mod.desc}</p>
                  </div>
                  <div className="pt-2 border-t border-slate-50 flex items-center text-xs text-slate-400">
                    <span>Target: Phase Milestone</span>
                    <ArrowRight className="w-3 h-3 ml-1" />
                  </div>
                </div>
              );
            })}
          </div>
        </section>
      </main>

      {/* Footer */}
      <footer className="bg-white border-t border-slate-200 py-6 mt-auto">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 flex flex-col sm:flex-row items-center justify-between text-xs text-slate-500 gap-4">
          <p>© {new Date().getFullYear()} CampusVenue — Enterprise University Venue & Event Platform</p>
          <div className="flex items-center space-x-6">
            <a href="/swagger" className="hover:text-slate-800 transition">API Documentation</a>
            <a href="/health" className="hover:text-slate-800 transition">Health Status</a>
            <span>Phase 0: Enterprise Foundation</span>
          </div>
        </div>
      </footer>
    </div>
  );
};

export default App;
