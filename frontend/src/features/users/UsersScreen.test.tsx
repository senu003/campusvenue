import React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { UsersScreen } from './UsersScreen';
import { AuthContext, type AuthContextValue } from '../auth/context';
import { setCachedAntiforgeryToken } from '../auth/api';
import type { User } from '../auth/types';
import type { UserDirectoryResponse } from './types';

const mockUsersData: UserDirectoryResponse = {
  items: [
    {
      id: '11111111-1111-1111-1111-111111111111',
      email: 'admin@campus.internal',
      roles: ['UniversityAdmin'],
      emailConfirmed: true,
      twoFactorEnabled: true,
      createdAtUtc: '2026-11-18T10:00:00Z',
    },
    {
      id: '22222222-2222-2222-2222-222222222222',
      email: 'student@campus.internal',
      roles: ['Participant'],
      emailConfirmed: true,
      twoFactorEnabled: false,
      createdAtUtc: '2026-11-18T11:00:00Z',
    },
  ],
  page: 1,
  pageSize: 10,
  totalCount: 2,
  totalPages: 1,
};

function renderWithAuth(
  ui: React.ReactElement,
  authOverrides?: Partial<AuthContextValue>
) {
  const defaultAuthValue: AuthContextValue = {
    user: null,
    isAuthenticated: false,
    isLoading: false,
    error: null,
    pendingTwoFactor: false,
    login: vi.fn(),
    verifyChallenge: vi.fn(),
    cancelTwoFactorChallenge: vi.fn(),
    logout: vi.fn(),
    checkSession: vi.fn(),
    clearSession: vi.fn(),
    clearError: vi.fn(),
    ...authOverrides,
  };

  return render(
    <AuthContext.Provider value={defaultAuthValue}>
      {ui}
    </AuthContext.Provider>
  );
}

describe('UsersScreen Component Tests', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    setCachedAntiforgeryToken(null);
  });

  it('renders Access Denied view when signed-out', async () => {
    renderWithAuth(<UsersScreen />);

    expect(screen.getByTestId('users-access-denied')).toBeInTheDocument();
    expect(screen.getByText(/Access Denied: University Administrator Privilege Required/i)).toBeInTheDocument();
    expect(screen.getByText(/Signed Out/i)).toBeInTheDocument();
  });

  it('renders Access Denied view when user is a Participant (not UniversityAdmin)', async () => {
    const participantUser: User = {
      id: '22222222-2222-2222-2222-222222222222',
      email: 'participant@campus.internal',
      twoFactorEnabled: true,
      roles: ['Participant'],
      mfaCompleted: true,
    };

    renderWithAuth(<UsersScreen />, {
      user: participantUser,
      isAuthenticated: true,
      isLoading: false,
    });

    expect(screen.getByTestId('users-access-denied')).toBeInTheDocument();
    expect(screen.getByText(/Requires UniversityAdmin/i)).toBeInTheDocument();
  });

  it('renders Access Denied view when user is UniversityAdmin but session has NOT completed MFA', async () => {
    const adminWithoutMfa: User = {
      id: '11111111-1111-1111-1111-111111111111',
      email: 'admin@campus.internal',
      twoFactorEnabled: true,
      roles: ['UniversityAdmin'],
      mfaCompleted: false, // Session did not complete MFA challenge
    };

    renderWithAuth(<UsersScreen />, {
      user: adminWithoutMfa,
      isAuthenticated: true,
      isLoading: false,
    });

    expect(screen.getByTestId('users-access-denied')).toBeInTheDocument();
    expect(screen.getByText(/current session was established with password only/i)).toBeInTheDocument();
  });

  it('renders User Directory table when user is UniversityAdmin with completed MFA session', async () => {
    const adminWithMfa: User = {
      id: '11111111-1111-1111-1111-111111111111',
      email: 'admin@campus.internal',
      twoFactorEnabled: true,
      roles: ['UniversityAdmin'],
      mfaCompleted: true,
    };

    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => mockUsersData,
    } as Response);

    renderWithAuth(<UsersScreen />, {
      user: adminWithMfa,
      isAuthenticated: true,
      isLoading: false,
    });

    await waitFor(() => {
      expect(screen.getByTestId('users-table')).toBeInTheDocument();
      expect(screen.getByText('admin@campus.internal')).toBeInTheDocument();
      expect(screen.getByText('student@campus.internal')).toBeInTheDocument();
      expect(screen.getByTestId('role-badge-admin')).toBeInTheDocument();
      expect(screen.getByTestId('role-badge-participant')).toBeInTheDocument();
      expect(screen.getByTestId('total-users-count')).toHaveTextContent('2');
    });
  });

  it('handles search input and submits search filter query', async () => {
    const adminWithMfa: User = {
      id: '11111111-1111-1111-1111-111111111111',
      email: 'admin@campus.internal',
      twoFactorEnabled: true,
      roles: ['UniversityAdmin'],
      mfaCompleted: true,
    };

    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => mockUsersData,
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({
          ...mockUsersData,
          items: [mockUsersData.items[0]],
          totalCount: 1,
        }),
      } as Response);

    renderWithAuth(<UsersScreen />, {
      user: adminWithMfa,
      isAuthenticated: true,
      isLoading: false,
    });

    await waitFor(() => {
      expect(screen.getByTestId('users-table')).toBeInTheDocument();
    });

    // Enter search query
    const searchInput = screen.getByTestId('users-search-input');
    fireEvent.change(searchInput, { target: { value: 'admin@campus' } });

    const searchButton = screen.getByTestId('users-search-button');
    fireEvent.click(searchButton);

    await waitFor(() => {
      expect(fetchSpy).toHaveBeenCalledWith(
        expect.stringContaining('search=admin%40campus'),
        expect.anything()
      );
    });
  });

  it('renders empty state when no users match search filter', async () => {
    const adminWithMfa: User = {
      id: '11111111-1111-1111-1111-111111111111',
      email: 'admin@campus.internal',
      twoFactorEnabled: true,
      roles: ['UniversityAdmin'],
      mfaCompleted: true,
    };

    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({
        items: [],
        page: 1,
        pageSize: 10,
        totalCount: 0,
        totalPages: 0,
      }),
    } as Response);

    renderWithAuth(<UsersScreen />, {
      user: adminWithMfa,
      isAuthenticated: true,
      isLoading: false,
    });

    await waitFor(() => {
      expect(screen.getByText('No Users Found')).toBeInTheDocument();
    });
  });

  it('renders error notice with retry button when API fails', async () => {
    const adminWithMfa: User = {
      id: '11111111-1111-1111-1111-111111111111',
      email: 'admin@campus.internal',
      twoFactorEnabled: true,
      roles: ['UniversityAdmin'],
      mfaCompleted: true,
    };

    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false,
      status: 500,
    } as Response);

    renderWithAuth(<UsersScreen />, {
      user: adminWithMfa,
      isAuthenticated: true,
      isLoading: false,
    });

    await waitFor(() => {
      expect(screen.getByText('Failed to Load User Directory')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /Retry/i })).toBeInTheDocument();
    });
  });

  it('renders Manage roles for eligible rows and Operator Managed for UniversityAdmin', async () => {
    const adminWithMfa: User = {
      id: '11111111-1111-1111-1111-111111111111',
      email: 'admin@campus.internal',
      twoFactorEnabled: true,
      roles: ['UniversityAdmin'],
      mfaCompleted: true,
    };

    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => mockUsersData,
    } as Response);

    renderWithAuth(<UsersScreen />, {
      user: adminWithMfa,
      isAuthenticated: true,
      isLoading: false,
    });

    await waitFor(() => {
      // UniversityAdmin row should show "Operator Managed"
      expect(screen.getByTestId('role-managed-cli-11111111-1111-1111-1111-111111111111')).toBeInTheDocument();
      // Participant row should have Manage roles button
      expect(screen.getByTestId('manage-roles-btn-22222222-2222-2222-2222-222222222222')).toBeInTheDocument();
    });
  });

  it('opens Manage roles modal and successfully assigns staff roles, refreshing the row', async () => {
    const adminWithMfa: User = {
      id: '11111111-1111-1111-1111-111111111111',
      email: 'admin@campus.internal',
      twoFactorEnabled: true,
      roles: ['UniversityAdmin'],
      mfaCompleted: true,
    };

    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      // 1. Initial directory load
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => mockUsersData,
      } as Response)
      // 2. Antiforgery token fetch if needed
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({ requestToken: 'test-xsrf-token' }),
      } as Response)
      // 3. PUT /api/users/{id}/roles
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({
          userId: '22222222-2222-2222-2222-222222222222',
          roles: ['FacilityAdmin'],
          concurrencyStamp: 'new-stamp-123',
          message: 'User roles updated successfully.',
        }),
      } as Response);

    renderWithAuth(<UsersScreen />, {
      user: adminWithMfa,
      isAuthenticated: true,
      isLoading: false,
    });

    await waitFor(() => {
      expect(screen.getByTestId('manage-roles-btn-22222222-2222-2222-2222-222222222222')).toBeInTheDocument();
    });

    // Open manage modal
    fireEvent.click(screen.getByTestId('manage-roles-btn-22222222-2222-2222-2222-222222222222'));

    expect(screen.getByTestId('manage-roles-modal')).toBeInTheDocument();
    expect(screen.getAllByText('student@campus.internal').length).toBeGreaterThanOrEqual(2);

    // Select FacilityAdmin
    const facilityCheckbox = screen.getByTestId('role-checkbox-FacilityAdmin');
    expect(facilityCheckbox).not.toBeChecked();
    fireEvent.click(facilityCheckbox);
    expect(facilityCheckbox).toBeChecked();

    // Confirm changes
    const confirmBtn = screen.getByTestId('confirm-roles-btn');
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(screen.queryByTestId('manage-roles-modal')).not.toBeInTheDocument();
      // Updated badge in table row
      expect(screen.getByTestId('role-badge-facility')).toBeInTheDocument();
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      '/api/users/22222222-2222-2222-2222-222222222222/roles',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({
          roles: ['FacilityAdmin'],
          expectedRoles: ['Participant'],
        }),
      })
    );
  });

  it('detects 409 conflict, explains discrepancy, reloads current roles, and allows retry', async () => {
    const adminWithMfa: User = {
      id: '11111111-1111-1111-1111-111111111111',
      email: 'admin@campus.internal',
      twoFactorEnabled: true,
      roles: ['UniversityAdmin'],
      mfaCompleted: true,
    };

    vi.spyOn(globalThis, 'fetch')
      // 1. Initial directory load
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => mockUsersData,
      } as Response)
      // 2. Antiforgery token
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({ requestToken: 'test-xsrf-token' }),
      } as Response)
      // 3. PUT returns 409 Conflict (another admin modified target user)
      .mockResolvedValueOnce({
        ok: false,
        status: 409,
        json: async () => ({
          title: 'Conflicting Update',
          detail: 'The user roles were modified by another administrator.',
        }),
      } as Response)
      // 4. Reload user directory for this user
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({
          items: [
            {
              ...mockUsersData.items[1],
              roles: ['EventCoordinator'],
              concurrencyStamp: 'stamp-from-other-admin',
            },
          ],
          totalCount: 1,
          page: 1,
          pageSize: 10,
          totalPages: 1,
        }),
      } as Response)
      // 5. Retry PUT succeeds
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({
          userId: '22222222-2222-2222-2222-222222222222',
          roles: ['FacilityAdmin', 'EventCoordinator'],
          concurrencyStamp: 'final-stamp',
          message: 'User roles updated successfully.',
        }),
      } as Response);

    renderWithAuth(<UsersScreen />, {
      user: adminWithMfa,
      isAuthenticated: true,
      isLoading: false,
    });

    await waitFor(() => {
      expect(screen.getByTestId('manage-roles-btn-22222222-2222-2222-2222-222222222222')).toBeInTheDocument();
    });

    // Open modal
    fireEvent.click(screen.getByTestId('manage-roles-btn-22222222-2222-2222-2222-222222222222'));

    // Check FacilityAdmin and submit
    fireEvent.click(screen.getByTestId('role-checkbox-FacilityAdmin'));
    fireEvent.click(screen.getByTestId('confirm-roles-btn'));

    // Verify 409 Conflict is handled
    await waitFor(() => {
      expect(screen.getByTestId('role-conflict-alert')).toBeInTheDocument();
      expect(screen.getByText(/The user roles were modified by another administrator/i)).toBeInTheDocument();
    });

    // Verify that the reloaded roles from server (EventCoordinator) are now reflected in the modal
    await waitFor(() => {
      expect(screen.getByTestId('role-checkbox-EventCoordinator')).toBeChecked();
    });

    // Admin now also checks FacilityAdmin and retries
    fireEvent.click(screen.getByTestId('role-checkbox-FacilityAdmin'));
    fireEvent.click(screen.getByTestId('confirm-roles-btn'));

    // Success: modal closes and both roles appear in table row
    await waitFor(() => {
      expect(screen.queryByTestId('manage-roles-modal')).not.toBeInTheDocument();
      expect(screen.getByTestId('role-badge-coordinator')).toBeInTheDocument();
      expect(screen.getByTestId('role-badge-facility')).toBeInTheDocument();
    });
  });

  it('cancels role management modal without modifying user roles', async () => {
    const adminWithMfa: User = {
      id: '11111111-1111-1111-1111-111111111111',
      email: 'admin@campus.internal',
      twoFactorEnabled: true,
      roles: ['UniversityAdmin'],
      mfaCompleted: true,
    };

    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => mockUsersData,
    } as Response);

    renderWithAuth(<UsersScreen />, {
      user: adminWithMfa,
      isAuthenticated: true,
      isLoading: false,
    });

    await waitFor(() => {
      expect(screen.getByTestId('manage-roles-btn-22222222-2222-2222-2222-222222222222')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('manage-roles-btn-22222222-2222-2222-2222-222222222222'));
    expect(screen.getByTestId('manage-roles-modal')).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('cancel-roles-btn'));
    expect(screen.queryByTestId('manage-roles-modal')).not.toBeInTheDocument();
  });
});
