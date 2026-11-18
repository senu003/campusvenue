import type { UserDirectoryResponse, UserDirectoryParams } from './types';

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
