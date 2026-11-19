import type {
  UserDirectoryResponse,
  UserDirectoryParams,
  AssignUserRolesRequest,
  AssignUserRolesResponse,
} from './types';
import { fetchAntiforgeryToken, getCachedAntiforgeryToken, setCachedAntiforgeryToken } from '../auth/api';

export class DirectoryForbiddenError extends Error {
  statusCode: number;

  constructor(message = 'Access denied: University Administrator privileges and a completed multi-factor authentication (MFA) session are required.') {
    super(message);
    this.name = 'DirectoryForbiddenError';
    this.statusCode = 403;
  }
}

export class DirectoryAuthError extends Error {
  statusCode: number;

  constructor(message = 'Authentication required. Please sign in.') {
    super(message);
    this.name = 'DirectoryAuthError';
    this.statusCode = 401;
  }
}

export class RoleAssignmentConflictError extends Error {
  statusCode: number;

  constructor(message = 'Conflicting update: Another administrator modified this user’s roles. The latest roles have been reloaded for your review.') {
    super(message);
    this.name = 'RoleAssignmentConflictError';
    this.statusCode = 409;
  }
}

export async function fetchUserDirectory(params?: UserDirectoryParams): Promise<UserDirectoryResponse> {
  const queryParams = new URLSearchParams();
  if (params?.page) {
    queryParams.set('page', params.page.toString());
  }
  if (params?.pageSize) {
    queryParams.set('pageSize', params.pageSize.toString());
  }
  if (params?.search && params.search.trim()) {
    queryParams.set('search', params.search.trim());
  }

  const query = queryParams.toString();
  const url = query ? `/api/users?${query}` : '/api/users';

  const response = await fetch(url, {
    method: 'GET',
    headers: {
      'Accept': 'application/json',
    },
    credentials: 'include',
  });

  if (response.status === 401) {
    throw new DirectoryAuthError();
  }

  if (response.status === 403) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : undefined;
    throw new DirectoryForbiddenError(detail);
  }

  if (!response.ok) {
    throw new Error(`Failed to load institutional users (HTTP ${response.status})`);
  }

  return response.json();
}

export async function assignUserRoles(
  userId: string,
  data: AssignUserRolesRequest
): Promise<AssignUserRolesResponse> {
  let token = getCachedAntiforgeryToken();
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch(`/api/users/${userId}/roles`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify(data),
  });

  if (response.status === 401) {
    throw new DirectoryAuthError();
  }

  if (response.status === 403) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : undefined;
    throw new DirectoryForbiddenError(detail);
  }

  if (response.status === 409) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'Conflicting update: Another administrator modified this user’s roles.';
    throw new RoleAssignmentConflictError(detail);
  }

  if (response.status === 400) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'Role assignment validation failed.';
    if (detail.toLowerCase().includes('antiforgery')) {
      setCachedAntiforgeryToken(null);
    }
    throw new Error(detail);
  }

  if (!response.ok) {
    throw new Error(`Failed to update roles (HTTP ${response.status})`);
  }

  return response.json();
}
