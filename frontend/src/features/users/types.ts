export interface UserDirectoryItem {
  id: string;
  email: string;
  roles: string[];
  emailConfirmed: boolean;
  twoFactorEnabled: boolean;
  createdAtUtc: string;
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
