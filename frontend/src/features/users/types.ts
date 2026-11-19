export interface UserDirectoryItem {
  id: string;
  email: string;
  roles: string[];
  emailConfirmed: boolean;
  twoFactorEnabled: boolean;
  createdAtUtc: string;
  concurrencyStamp?: string;
}

export interface UserDirectoryResponse {
  items: UserDirectoryItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface UserDirectoryParams {
  page?: number;
  pageSize?: number;
  search?: string;
}

export interface AssignUserRolesRequest {
  roles: string[];
  concurrencyStamp?: string;
  expectedRoles?: string[];
}

export interface AssignUserRolesResponse {
  userId: string;
  roles: string[];
  concurrencyStamp: string;
  message: string;
}
