import React, { useState, useEffect } from 'react';
import {
  Users as UsersIcon,
  Search,
  RefreshCw,
  AlertCircle,
  AlertTriangle,
  ShieldCheck,
  ShieldAlert,
  ShieldOff,
  CheckCircle2,
  Clock,
  ChevronLeft,
  ChevronRight,
  UserCheck,
  Lock,
  UserCog,
  X,
  Check,
} from 'lucide-react';
import { useAuth } from '../auth/useAuth';
import type { UserDirectoryItem, UserDirectoryResponse } from './types';
import {
  fetchUserDirectory,
  assignUserRoles,
  DirectoryForbiddenError,
  DirectoryAuthError,
  RoleAssignmentConflictError,
} from './api';

interface UsersScreenProps {
  onNavigateToAccount?: () => void;
  onNavigateToLogin?: () => void;
}

export const UsersScreen: React.FC<UsersScreenProps> = ({
  onNavigateToAccount,
  onNavigateToLogin,
}) => {
  const { user, isAuthenticated, isLoading: authLoading } = useAuth();

  const [users, setUsers] = useState<UserDirectoryItem[]>([]);
  const [page, setPage] = useState<number>(1);
  const [pageSize] = useState<number>(10);
  const [totalCount, setTotalCount] = useState<number>(0);
  const [totalPages, setTotalPages] = useState<number>(0);

  const [searchQuery, setSearchQuery] = useState<string>('');
  const [appliedSearch, setAppliedSearch] = useState<string>('');

  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [isApiForbidden, setIsApiForbidden] = useState<boolean>(false);
  const [reloadCounter, setReloadCounter] = useState<number>(0);

  // Modal State for Staff Role Management
  const [managingUser, setManagingUser] = useState<UserDirectoryItem | null>(null);
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);
  const [isSubmittingRoles, setIsSubmittingRoles] = useState<boolean>(false);
  const [isReloadingRoles, setIsReloadingRoles] = useState<boolean>(false);
  const [roleModalError, setRoleModalError] = useState<string | null>(null);
  const [conflictMessage, setConflictMessage] = useState<string | null>(null);

  const isUniversityAdmin = user?.roles?.includes('UniversityAdmin') ?? false;
  const isMfaCompleted = user?.mfaCompleted ?? false;
  const hasClientAccess = isAuthenticated && isUniversityAdmin && isMfaCompleted;
  const isAccessDenied = (!authLoading && !hasClientAccess) || isApiForbidden;

  const triggerReload = () => {
    setIsLoading(true);
    setReloadCounter((c) => c + 1);
  };

  useEffect(() => {
    let ignore = false;

    async function executeLoad() {
      if (!hasClientAccess) {
        return;
      }

      try {
        const data: UserDirectoryResponse = await fetchUserDirectory({
          page,
          pageSize,
          search: appliedSearch,
        });

        if (!ignore) {
          setUsers(data.items);
          setPage(data.page);
          setTotalCount(data.totalCount);
          setTotalPages(data.totalPages);
          setError(null);
          setIsApiForbidden(false);
        }
      } catch (err: unknown) {
        if (!ignore) {
          if (err instanceof DirectoryForbiddenError) {
            setIsApiForbidden(true);
            setError(err.message);
          } else if (err instanceof DirectoryAuthError) {
            setError('Your session has expired. Please sign in again.');
          } else {
            const msg = err instanceof Error ? err.message : 'Failed to retrieve institutional user directory.';
            setError(msg);
          }
        }
      } finally {
        if (!ignore) {
          setIsLoading(false);
        }
      }
    }

    if (!authLoading && hasClientAccess) {
      executeLoad();
    }

    return () => {
      ignore = true;
    };
  }, [authLoading, hasClientAccess, page, pageSize, appliedSearch, reloadCounter]);

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(1);
    setAppliedSearch(searchQuery.trim());
  };

  const handleClearSearch = () => {
    setSearchQuery('');
    setPage(1);
    setAppliedSearch('');
  };

  const handleOpenManageModal = (targetUser: UserDirectoryItem) => {
    setManagingUser(targetUser);
    setSelectedRoles(
      targetUser.roles.filter((r) => r === 'FacilityAdmin' || r === 'EventCoordinator')
    );
    setRoleModalError(null);
    setConflictMessage(null);
    setIsSubmittingRoles(false);
    setIsReloadingRoles(false);
  };

  const handleCloseManageModal = () => {
    if (isSubmittingRoles) return;
    setManagingUser(null);
    setSelectedRoles([]);
    setRoleModalError(null);
    setConflictMessage(null);
  };

  const handleToggleRole = (role: 'FacilityAdmin' | 'EventCoordinator') => {
    setSelectedRoles((prev) =>
      prev.includes(role) ? prev.filter((r) => r !== role) : [...prev, role]
    );
  };

  const handleConfirmRoleChanges = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!managingUser) return;

    setIsSubmittingRoles(true);
    setRoleModalError(null);
    setConflictMessage(null);

    try {
      const response = await assignUserRoles(managingUser.id, {
        roles: selectedRoles,
        concurrencyStamp: managingUser.concurrencyStamp,
        expectedRoles: managingUser.roles,
      });

      // Update row in state upon success
      setUsers((prev) =>
        prev.map((u) =>
          u.id === managingUser.id
            ? {
                ...u,
                roles: response.roles,
                concurrencyStamp: response.concurrencyStamp,
              }
            : u
        )
      );

      handleCloseManageModal();
    } catch (err: unknown) {
      if (err instanceof RoleAssignmentConflictError) {
        setConflictMessage(
          err.message ||
            'Another administrator modified this user’s roles. The latest roles have been reloaded for your review.'
        );

        // Reload current user state from server
        setIsReloadingRoles(true);
        try {
          const freshData = await fetchUserDirectory({
            search: managingUser.email,
            page: 1,
            pageSize: 10,
          });
          const freshUser = freshData.items.find((u) => u.id === managingUser.id);
          if (freshUser) {
            setManagingUser(freshUser);
            setSelectedRoles(
              freshUser.roles.filter((r) => r === 'FacilityAdmin' || r === 'EventCoordinator')
            );
            setUsers((prev) =>
              prev.map((u) => (u.id === freshUser.id ? freshUser : u))
            );
          }
        } catch {
          // If directory reload fails, retain conflict message
        } finally {
          setIsReloadingRoles(false);
        }
      } else if (err instanceof DirectoryForbiddenError) {
        setRoleModalError(err.message);
      } else if (err instanceof DirectoryAuthError) {
        setRoleModalError('Your session has expired. Please sign in again.');
      } else {
        const msg = err instanceof Error ? err.message : 'Failed to update roles.';
        setRoleModalError(msg);
      }
    } finally {
      setIsSubmittingRoles(false);
    }
  };

  const renderRoleBadge = (role: string) => {
    switch (role) {
      case 'UniversityAdmin':
        return (
          <span
            key={role}
            data-testid="role-badge-admin"
            className="inline-flex items-center px-2 py-0.5 rounded-full text-2xs font-bold bg-purple-100 text-purple-800 border border-purple-200"
          >
            UniversityAdmin
          </span>
        );
      case 'FacilityAdmin':
        return (
          <span
            key={role}
            data-testid="role-badge-facility"
            className="inline-flex items-center px-2 py-0.5 rounded-full text-2xs font-bold bg-blue-100 text-blue-800 border border-blue-200"
          >
            FacilityAdmin
          </span>
        );
      case 'EventCoordinator':
        return (
          <span
            key={role}
            data-testid="role-badge-coordinator"
            className="inline-flex items-center px-2 py-0.5 rounded-full text-2xs font-bold bg-amber-100 text-amber-800 border border-amber-200"
          >
            EventCoordinator
          </span>
        );
      case 'Participant':
      default:
        return (
          <span
            key={role}
            data-testid="role-badge-participant"
            className="inline-flex items-center px-2 py-0.5 rounded-full text-2xs font-bold bg-slate-100 text-slate-700 border border-slate-200"
          >
            Participant
          </span>
        );
    }
  };

  // 1. Access Denied / Forbidden State
  if (!authLoading && isAccessDenied) {
    return (
      <div className="max-w-3xl mx-auto space-y-6">
        <div
          role="alert"
          data-testid="users-access-denied"
          className="bg-white border border-rose-200 rounded-2xl p-6 sm:p-8 shadow-sm space-y-6"
        >
          <div className="flex items-start space-x-4">
            <div className="w-12 h-12 rounded-xl bg-rose-100 flex items-center justify-center text-rose-600 shrink-0">
              <ShieldAlert className="w-6 h-6" />
            </div>
            <div className="space-y-2">
              <span className="px-2.5 py-0.5 text-xs font-bold uppercase tracking-wider bg-rose-100 text-rose-800 rounded-md">
                HTTP 403 Forbidden
              </span>
              <h2 className="text-xl font-extrabold text-slate-900 tracking-tight">
                Access Denied: University Administrator Privilege Required
              </h2>
              <p className="text-sm text-slate-600 leading-relaxed">
                Access to the institutional user directory requires both the{' '}
                <strong className="text-slate-900 font-semibold">UniversityAdmin</strong> role and an active session
                that has <strong className="text-slate-900 font-semibold">actually completed multi-factor authentication (MFA)</strong>.
                MFA enrollment alone does not satisfy this security policy.
              </p>
            </div>
          </div>

          <div className="bg-slate-50 border border-slate-200 rounded-xl p-4 text-xs space-y-2 text-slate-700">
            <h3 className="font-bold text-slate-900 uppercase tracking-wider text-2xs">Security Evaluation Details:</h3>
            <ul className="list-disc list-inside space-y-1 text-slate-600">
              <li>
                Authentication Status:{' '}
                {isAuthenticated ? (
                  <span className="font-semibold text-emerald-700">Authenticated ({user?.email})</span>
                ) : (
                  <span className="font-semibold text-rose-700">Signed Out</span>
                )}
              </li>
              <li>
                Assigned Role:{' '}
                <span className="font-semibold text-slate-800">
                  {user?.roles && user.roles.length > 0 ? user.roles.join(', ') : 'None / Participant'}
                </span>{' '}
                {isUniversityAdmin ? (
                  <span className="text-emerald-700 font-medium">(UniversityAdmin granted)</span>
                ) : (
                  <span className="text-rose-700 font-medium">(Requires UniversityAdmin)</span>
                )}
              </li>
              <li>
                Session Multi-Factor State:{' '}
                {isMfaCompleted ? (
                  <span className="font-semibold text-emerald-700">Completed (MFA Challenge Verified)</span>
                ) : user?.twoFactorEnabled ? (
                  <span className="font-semibold text-amber-700">
                    Enrolled, but current session was established with password only. Please sign out and sign in with your authenticator code.
                  </span>
                ) : (
                  <span className="font-semibold text-rose-700">Not enrolled in MFA</span>
                )}
              </li>
            </ul>
          </div>

          <div className="flex flex-wrap gap-3 pt-2">
            {isAuthenticated ? (
              <button
                type="button"
                onClick={onNavigateToAccount}
                className="inline-flex items-center space-x-2 px-4 py-2 bg-sky-600 hover:bg-sky-700 text-white rounded-lg text-xs font-bold shadow-sm transition cursor-pointer"
              >
                <UserCheck className="w-4 h-4" />
                <span>Go to My Account (Security Settings)</span>
              </button>
            ) : (
              <button
                type="button"
                onClick={onNavigateToLogin}
                className="inline-flex items-center space-x-2 px-4 py-2 bg-sky-600 hover:bg-sky-700 text-white rounded-lg text-xs font-bold shadow-sm transition cursor-pointer"
              >
                <Lock className="w-4 h-4" />
                <span>Sign In as University Administrator</span>
              </button>
            )}
            <button
              type="button"
              onClick={() => {
                if (hasClientAccess) {
                  triggerReload();
                }
              }}
              className="inline-flex items-center space-x-2 px-4 py-2 bg-slate-100 hover:bg-slate-200 text-slate-700 rounded-lg text-xs font-bold transition cursor-pointer"
            >
              <RefreshCw className="w-3.5 h-3.5" />
              <span>Retry Authorization Check</span>
            </button>
          </div>
        </div>
      </div>
    );
  }

  // Calculate proposed role delta for confirmation UI
  const currentStaffRoles: string[] = managingUser
    ? managingUser.roles.filter((r) => r === 'FacilityAdmin' || r === 'EventCoordinator')
    : [];
  const rolesToAdd = selectedRoles.filter((r) => !currentStaffRoles.includes(r));
  const rolesToRemove = currentStaffRoles.filter((r) => !selectedRoles.includes(r));
  const hasRoleChanges = rolesToAdd.length > 0 || rolesToRemove.length > 0;

  return (
    <div className="max-w-6xl mx-auto space-y-6">
      {/* Header Banner */}
      <section className="bg-white border border-slate-200 rounded-2xl p-6 shadow-sm">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 pb-4 border-b border-slate-100">
          <div className="space-y-1">
            <div className="flex items-center space-x-2">
              <span className="px-2.5 py-0.5 text-xs font-bold uppercase tracking-wider bg-purple-100 text-purple-800 rounded-md">
                Phase 1B.2 Staff Roles
              </span>
              <span className="text-xs text-slate-400">|</span>
              <span className="text-xs font-semibold text-slate-600">Administrative Directory & Role Management</span>
            </div>
            <h1 className="text-2xl font-extrabold text-slate-900 tracking-tight flex items-center space-x-2">
              <UsersIcon className="w-6 h-6 text-purple-600" />
              <span>Institutional User Directory</span>
            </h1>
            <p className="text-xs text-slate-500">
              Administrative overview of university accounts, confirmation status, assigned roles, and MFA security posture.
            </p>
          </div>

          <div className="flex items-center space-x-2">
            <span className="inline-flex items-center space-x-1.5 px-3 py-1 bg-emerald-50 text-emerald-800 border border-emerald-200 rounded-lg text-xs font-semibold">
              <ShieldCheck className="w-4 h-4 text-emerald-600" />
              <span>MFA Session Active</span>
            </span>
            <button
              type="button"
              onClick={triggerReload}
              disabled={isLoading}
              className="p-2 text-slate-500 hover:text-slate-700 hover:bg-slate-100 rounded-lg border border-slate-200 transition disabled:opacity-50 cursor-pointer"
              title="Refresh directory"
            >
              <RefreshCw className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} />
            </button>
          </div>
        </div>

        {/* Search & Filter Toolbar */}
        <div className="pt-4 flex flex-col sm:flex-row gap-3 items-stretch sm:items-center justify-between">
          <form onSubmit={handleSearchSubmit} className="flex-1 max-w-md flex items-center space-x-2">
            <div className="relative flex-1">
              <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
              <input
                type="text"
                data-testid="users-search-input"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="Search users by university email..."
                className="w-full pl-9 pr-3 py-2 text-xs border border-slate-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-purple-500 focus:border-transparent bg-slate-50 focus:bg-white transition"
              />
            </div>
            <button
              type="submit"
              data-testid="users-search-button"
              disabled={isLoading}
              className="px-3.5 py-2 bg-purple-600 hover:bg-purple-700 text-white rounded-lg text-xs font-semibold shadow-xs transition disabled:opacity-50 cursor-pointer"
            >
              Search
            </button>
            {appliedSearch && (
              <button
                type="button"
                onClick={handleClearSearch}
                className="px-2.5 py-2 bg-slate-100 hover:bg-slate-200 text-slate-600 rounded-lg text-xs font-medium transition cursor-pointer"
              >
                Clear
              </button>
            )}
          </form>

          <div className="text-xs text-slate-500 flex items-center space-x-2">
            <span>Total Accounts:</span>
            <span data-testid="total-users-count" className="font-bold text-slate-900 bg-slate-100 px-2 py-0.5 rounded-md border border-slate-200">
              {totalCount}
            </span>
          </div>
        </div>
      </section>

      {/* Error Notice */}
      {error && !isAccessDenied && (
        <div role="alert" className="p-4 bg-rose-50 border border-rose-200 rounded-xl flex items-start space-x-3 text-rose-800">
          <AlertCircle className="w-5 h-5 text-rose-600 shrink-0 mt-0.5" />
          <div className="space-y-1 flex-1">
            <h2 className="text-sm font-bold">Failed to Load User Directory</h2>
            <p className="text-xs leading-relaxed">{error}</p>
          </div>
          <button
            type="button"
            onClick={triggerReload}
            className="text-xs font-semibold text-rose-800 hover:underline cursor-pointer"
          >
            Retry
          </button>
        </div>
      )}

      {/* Directory Content Table Card */}
      <section className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
        {isLoading ? (
          <div className="py-16 flex flex-col items-center justify-center space-y-3 text-slate-500">
            <RefreshCw className="w-6 h-6 animate-spin text-purple-600" />
            <p className="text-xs font-medium">Loading institutional users...</p>
          </div>
        ) : users.length === 0 ? (
          <div className="py-16 text-center space-y-3">
            <div className="w-12 h-12 rounded-full bg-slate-100 text-slate-400 mx-auto flex items-center justify-center">
              <UsersIcon className="w-6 h-6" />
            </div>
            <div className="space-y-1">
              <h3 className="text-sm font-bold text-slate-900">No Users Found</h3>
              <p className="text-xs text-slate-500 max-w-sm mx-auto">
                {appliedSearch
                  ? `No university user accounts matched the filter "${appliedSearch}".`
                  : 'There are currently no registered users in the directory.'}
              </p>
            </div>
            {appliedSearch && (
              <button
                type="button"
                onClick={handleClearSearch}
                className="text-xs text-purple-600 hover:text-purple-800 font-semibold underline cursor-pointer"
              >
                Clear Search Filter
              </button>
            )}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table data-testid="users-table" className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-slate-50/80 border-b border-slate-200 text-2xs font-bold uppercase tracking-wider text-slate-500">
                  <th scope="col" className="py-3 px-4 sm:px-6">University Email</th>
                  <th scope="col" className="py-3 px-4">Assigned Roles</th>
                  <th scope="col" className="py-3 px-4">Two-Factor (MFA)</th>
                  <th scope="col" className="py-3 px-4">Email Status</th>
                  <th scope="col" className="py-3 px-4">Created (UTC)</th>
                  <th scope="col" className="py-3 px-4 sm:px-6 text-right">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 text-xs text-slate-700 font-normal">
                {users.map((item) => {
                  const isEligible = Boolean(!item.roles || !item.roles.includes('UniversityAdmin'));
                  const displayRoles = item.roles && item.roles.length > 0 ? item.roles : ['Participant'];

                  return (
                    <tr key={item.id} data-testid={`user-row-${item.id}`} className="hover:bg-slate-50/50 transition">
                      {/* Email */}
                      <td className="py-3.5 px-4 sm:px-6 font-medium text-slate-900">
                        <div className="flex items-center space-x-2">
                          <span className="font-mono">{item.email}</span>
                          {item.email === user?.email && (
                            <span className="text-2xs font-bold px-1.5 py-0.5 rounded bg-sky-100 text-sky-800 border border-sky-200">
                              You
                            </span>
                          )}
                        </div>
                      </td>

                      {/* Roles */}
                      <td className="py-3.5 px-4">
                        <div className="flex flex-wrap gap-1.5" data-testid={`user-roles-${item.id}`}>
                          {displayRoles.map((r) => renderRoleBadge(r))}
                        </div>
                      </td>

                      {/* MFA Status */}
                      <td className="py-3.5 px-4">
                        {item.twoFactorEnabled ? (
                          <span className="inline-flex items-center space-x-1 text-2xs font-semibold text-emerald-700 bg-emerald-50 border border-emerald-200 px-2 py-0.5 rounded-full">
                            <ShieldCheck className="w-3 h-3 text-emerald-600" />
                            <span>Active</span>
                          </span>
                        ) : (
                          <span className="inline-flex items-center space-x-1 text-2xs font-semibold text-slate-500 bg-slate-50 border border-slate-200 px-2 py-0.5 rounded-full">
                            <ShieldOff className="w-3 h-3 text-slate-400" />
                            <span>Disabled</span>
                          </span>
                        )}
                      </td>

                      {/* Email Status */}
                      <td className="py-3.5 px-4">
                        {item.emailConfirmed ? (
                          <span className="inline-flex items-center space-x-1 text-2xs font-semibold text-emerald-700">
                            <CheckCircle2 className="w-3.5 h-3.5 text-emerald-600" />
                            <span>Confirmed</span>
                          </span>
                        ) : (
                          <span className="inline-flex items-center space-x-1 text-2xs font-semibold text-amber-700">
                            <Clock className="w-3.5 h-3.5 text-amber-600" />
                            <span>Unconfirmed</span>
                          </span>
                        )}
                      </td>

                      {/* Created Date */}
                      <td className="py-3.5 px-4 text-slate-500 font-mono text-2xs">
                        {item.createdAtUtc ? new Date(item.createdAtUtc).toLocaleDateString(undefined, {
                          year: 'numeric',
                          month: 'short',
                          day: 'numeric',
                        }) : '—'}
                      </td>

                      {/* Action Column */}
                      <td className="py-3.5 px-4 sm:px-6 text-right">
                        {isEligible ? (
                          <button
                            type="button"
                            data-testid={`manage-roles-btn-${item.id}`}
                            onClick={() => handleOpenManageModal(item)}
                            className="inline-flex items-center space-x-1.5 px-2.5 py-1 text-xs font-semibold text-purple-700 bg-purple-50 hover:bg-purple-100 border border-purple-200 rounded-lg transition cursor-pointer"
                          >
                            <UserCog className="w-3.5 h-3.5 text-purple-600" />
                            <span>Manage roles</span>
                          </button>
                        ) : (
                          <span
                            data-testid={`role-managed-cli-${item.id}`}
                            className="text-2xs font-medium text-slate-400 italic"
                            title="UniversityAdmin elevation is restricted to operator CLI"
                          >
                            Operator Managed
                          </span>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination Footer */}
        {totalPages > 0 && (
          <div className="bg-slate-50/50 border-t border-slate-200 px-4 sm:px-6 py-3 flex flex-col sm:flex-row items-center justify-between gap-3 text-xs text-slate-500">
            <div>
              Showing <span className="font-semibold text-slate-900">{(page - 1) * pageSize + 1}</span> to{' '}
              <span className="font-semibold text-slate-900">{Math.min(page * pageSize, totalCount)}</span> of{' '}
              <span className="font-semibold text-slate-900">{totalCount}</span> users
            </div>

            <div className="flex items-center space-x-2">
              <span className="text-2xs font-medium text-slate-400 mr-1">
                Page {page} of {totalPages}
              </span>
              <button
                type="button"
                data-testid="users-prev-page"
                onClick={() => setPage((p) => Math.max(p - 1, 1))}
                disabled={page <= 1 || isLoading}
                className="inline-flex items-center space-x-1 px-2.5 py-1 text-xs font-semibold text-slate-700 bg-white border border-slate-300 rounded-md hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed transition cursor-pointer"
              >
                <ChevronLeft className="w-3.5 h-3.5" />
                <span>Previous</span>
              </button>
              <button
                type="button"
                data-testid="users-next-page"
                onClick={() => setPage((p) => Math.min(p + 1, totalPages))}
                disabled={page >= totalPages || isLoading}
                className="inline-flex items-center space-x-1 px-2.5 py-1 text-xs font-semibold text-slate-700 bg-white border border-slate-300 rounded-md hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed transition cursor-pointer"
              >
                <span>Next</span>
                <ChevronRight className="w-3.5 h-3.5" />
              </button>
            </div>
          </div>
        )}
      </section>

      {/* Manage Roles Modal Dialog */}
      {managingUser && (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="manage-roles-modal-title"
          data-testid="manage-roles-modal"
          className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs animate-in fade-in duration-150"
        >
          <div className="bg-white rounded-2xl max-w-lg w-full p-6 shadow-xl border border-slate-200 space-y-5">
            {/* Modal Header */}
            <div className="flex items-start justify-between pb-3 border-b border-slate-100">
              <div className="space-y-1">
                <div className="flex items-center space-x-2">
                  <span className="px-2 py-0.5 text-2xs font-bold uppercase tracking-wider bg-purple-100 text-purple-800 rounded-md">
                    Staff Authorization
                  </span>
                </div>
                <h2 id="manage-roles-modal-title" className="text-lg font-bold text-slate-900 flex items-center space-x-2">
                  <UserCog className="w-5 h-5 text-purple-600" />
                  <span>Manage Staff Roles</span>
                </h2>
                <p className="text-xs text-slate-500 font-mono">
                  {managingUser.email}
                </p>
              </div>
              <button
                type="button"
                data-testid="cancel-roles-x-btn"
                onClick={handleCloseManageModal}
                disabled={isSubmittingRoles}
                className="p-1 text-slate-400 hover:text-slate-600 rounded-lg hover:bg-slate-100 transition cursor-pointer"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            {/* Conflict Alert Banner */}
            {conflictMessage && (
              <div
                role="alert"
                data-testid="role-conflict-alert"
                className="p-3.5 bg-amber-50 border border-amber-200 rounded-xl text-amber-900 text-xs flex items-start space-x-3"
              >
                <AlertTriangle className="w-5 h-5 text-amber-600 shrink-0 mt-0.5" />
                <div className="space-y-1 flex-1">
                  <strong className="font-bold block">Conflicting Update Detected</strong>
                  <p className="leading-relaxed">{conflictMessage}</p>
                  {isReloadingRoles && (
                    <p className="text-2xs text-amber-700 italic flex items-center space-x-1 pt-1">
                      <RefreshCw className="w-3 h-3 animate-spin" />
                      <span>Reloading latest roles from server...</span>
                    </p>
                  )}
                </div>
              </div>
            )}

            {/* Error Alert */}
            {roleModalError && (
              <div
                role="alert"
                data-testid="role-error-alert"
                className="p-3 bg-rose-50 border border-rose-200 rounded-xl text-rose-800 text-xs flex items-start space-x-2"
              >
                <AlertCircle className="w-4 h-4 text-rose-600 shrink-0 mt-0.5" />
                <span className="flex-1">{roleModalError}</span>
              </div>
            )}

            {/* Current Roles Display */}
            <div className="space-y-1.5 bg-slate-50 p-3.5 rounded-xl border border-slate-200 text-xs">
              <span className="text-2xs font-bold uppercase tracking-wider text-slate-500 block">
                Current Assigned Roles
              </span>
              <div className="flex flex-wrap gap-1.5" data-testid="modal-current-roles">
                {managingUser.roles.map((r) => renderRoleBadge(r))}
              </div>
            </div>

            {/* Role Checkboxes */}
            <form onSubmit={handleConfirmRoleChanges} className="space-y-4">
              <div className="space-y-3">
                <span className="text-2xs font-bold uppercase tracking-wider text-slate-500 block">
                  Assign Staff Roles
                </span>

                {/* FacilityAdmin Checkbox Card */}
                <label
                  htmlFor="checkbox-facility-admin"
                  className={`flex items-start space-x-3 p-3 rounded-xl border transition cursor-pointer ${
                    selectedRoles.includes('FacilityAdmin')
                      ? 'bg-blue-50/50 border-blue-300 ring-1 ring-blue-300'
                      : 'bg-white border-slate-200 hover:bg-slate-50'
                  }`}
                >
                  <input
                    type="checkbox"
                    id="checkbox-facility-admin"
                    data-testid="role-checkbox-FacilityAdmin"
                    checked={selectedRoles.includes('FacilityAdmin')}
                    onChange={() => handleToggleRole('FacilityAdmin')}
                    disabled={isSubmittingRoles || isReloadingRoles}
                    className="mt-0.5 w-4 h-4 text-purple-600 rounded border-slate-300 focus:ring-purple-500"
                  />
                  <div className="space-y-0.5">
                    <div className="flex items-center space-x-2">
                      <span className="text-xs font-bold text-slate-900">FacilityAdmin</span>
                      <span className="text-2xs bg-blue-100 text-blue-800 px-1.5 py-0.2 rounded font-medium">
                        Facility Manager
                      </span>
                    </div>
                    <p className="text-2xs text-slate-500 leading-relaxed">
                      Administrative control over campus buildings, spaces, room setups, and maintenance scheduling.
                    </p>
                  </div>
                </label>

                {/* EventCoordinator Checkbox Card */}
                <label
                  htmlFor="checkbox-event-coordinator"
                  className={`flex items-start space-x-3 p-3 rounded-xl border transition cursor-pointer ${
                    selectedRoles.includes('EventCoordinator')
                      ? 'bg-amber-50/50 border-amber-300 ring-1 ring-amber-300'
                      : 'bg-white border-slate-200 hover:bg-slate-50'
                  }`}
                >
                  <input
                    type="checkbox"
                    id="checkbox-event-coordinator"
                    data-testid="role-checkbox-EventCoordinator"
                    checked={selectedRoles.includes('EventCoordinator')}
                    onChange={() => handleToggleRole('EventCoordinator')}
                    disabled={isSubmittingRoles || isReloadingRoles}
                    className="mt-0.5 w-4 h-4 text-purple-600 rounded border-slate-300 focus:ring-purple-500"
                  />
                  <div className="space-y-0.5">
                    <div className="flex items-center space-x-2">
                      <span className="text-xs font-bold text-slate-900">EventCoordinator</span>
                      <span className="text-2xs bg-amber-100 text-amber-800 px-1.5 py-0.2 rounded font-medium">
                        Organizer
                      </span>
                    </div>
                    <p className="text-2xs text-slate-500 leading-relaxed">
                      Authorized to submit venue reservation requests, publish institutional events, and manage attendees.
                    </p>
                  </div>
                </label>

                <p className="text-2xs text-slate-500 italic px-1">
                  Note: Participant access is baseline for all confirmed university accounts. If neither staff role is selected, the user retains standard Participant access.
                </p>
              </div>

              {/* Confirmation / Pending Delta Preview */}
              <div className="p-3 bg-slate-50 rounded-xl border border-slate-200 text-xs space-y-1">
                <span className="text-2xs font-bold uppercase tracking-wider text-slate-600 block">
                  Change Summary & Impact
                </span>
                {hasRoleChanges ? (
                  <div className="space-y-1 text-2xs">
                    {rolesToAdd.length > 0 && (
                      <div className="text-emerald-700 font-medium">
                        + Granting: <strong className="font-bold">{rolesToAdd.join(', ')}</strong>
                      </div>
                    )}
                    {rolesToRemove.length > 0 && (
                      <div className="text-rose-700 font-medium">
                        - Revoking: <strong className="font-bold">{rolesToRemove.join(', ')}</strong>
                      </div>
                    )}
                    <p className="text-slate-500 pt-1">
                      Applying this change will rotate the user’s security stamp, requiring existing sessions to revalidate.
                    </p>
                  </div>
                ) : (
                  <p className="text-2xs text-slate-500">
                    No changes selected. Submitting will keep the currently stored role set.
                  </p>
                )}
              </div>

              {/* Modal Actions */}
              <div className="flex items-center justify-end space-x-2 pt-2 border-t border-slate-100">
                <button
                  type="button"
                  data-testid="cancel-roles-btn"
                  onClick={handleCloseManageModal}
                  disabled={isSubmittingRoles}
                  className="px-4 py-2 text-xs font-semibold text-slate-700 bg-white border border-slate-300 hover:bg-slate-50 rounded-lg transition disabled:opacity-50 cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  data-testid="confirm-roles-btn"
                  disabled={isSubmittingRoles || isReloadingRoles}
                  className="inline-flex items-center space-x-1.5 px-4 py-2 bg-purple-600 hover:bg-purple-700 text-white text-xs font-bold rounded-lg shadow-sm transition disabled:opacity-50 cursor-pointer"
                >
                  {isSubmittingRoles ? (
                    <>
                      <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                      <span>Saving Changes...</span>
                    </>
                  ) : (
                    <>
                      <Check className="w-3.5 h-3.5" />
                      <span>Confirm Changes</span>
                    </>
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
