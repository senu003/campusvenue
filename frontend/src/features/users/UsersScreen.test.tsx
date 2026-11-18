import React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { UsersScreen } from './UsersScreen';
import { AuthContext, type AuthContextValue } from '../auth/context';
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
});
