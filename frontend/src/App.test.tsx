import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import App from './App';

describe('App & Authentication Slice Component Tests', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.history.pushState({}, '', '/');
  });

  const setupFetchMock = (options?: {
    healthOk?: boolean;
    authenticated?: boolean;
    loginSuccess?: boolean;
    loginRequiresTwoFactor?: boolean;
    emailUnconfirmed?: boolean;
    changePasswordError?: string;
    signOutEverywhereError?: string;
    twoFactorEnabled?: boolean;
    recoveryCodesLeft?: number;
    roles?: string[];
    mfaCompleted?: boolean;
    enrollError?: string;
    challengeError?: string;
    mailboxMessages?: Array<{ id: string; to: string; subject: string; bodyHtml: string; sentAtUtc: string }>;
  }) => {
    const {
      healthOk = true,
      authenticated = false,
      loginRequiresTwoFactor = false,
      emailUnconfirmed = false,
      changePasswordError,
      signOutEverywhereError,
      twoFactorEnabled = false,
      recoveryCodesLeft,
      roles,
      mfaCompleted = false,
      enrollError,
      challengeError,
      mailboxMessages = [],
    } = options || {};

    return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();

      if (url.includes('/health')) {
        if (!healthOk) {
          throw new Error('Network connection refused');
        }
        return {
          ok: true,
          status: 200,
          json: async () => ({
            status: 'Healthy',
            totalDurationMs: 4.2,
            timestamp: new Date().toISOString(),
            environment: 'Development',
            service: 'CampusVenue API',
            version: '1.0.0-phase1a.2',
            database: {
              status: 'Healthy',
              provider: 'PostgreSQL',
            },
            checks: [
              {
                name: 'postgresql',
                status: 'Healthy',
                description: 'Reachable and responsive',
                durationMs: 3.1,
              },
            ],
          }),
        } as Response;
      }

      if (url.includes('/api/auth/antiforgery')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ requestToken: 'test-mock-xsrf-token-123' }),
        } as Response;
      }

      if (url.includes('/api/auth/me')) {
        if (authenticated) {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              id: '11111111-2222-3333-4444-555555555555',
              email: 'coordinator@campus.internal',
              isAuthenticated: true,
              twoFactorEnabled: twoFactorEnabled ?? false,
              roles: roles ?? ['Participant'],
              mfaCompleted: mfaCompleted ?? false,
            }),
          } as Response;
        }
        return {
          ok: false,
          status: 401,
          json: async () => ({ title: 'Unauthorized' }),
        } as Response;
      }

      if (url.includes('/api/users')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            items: [
              {
                id: '11111111-2222-3333-4444-555555555555',
                email: 'admin@campus.internal',
                roles: ['UniversityAdmin'],
                emailConfirmed: true,
                twoFactorEnabled: true,
                createdAtUtc: '2026-11-18T10:00:00Z',
              },
            ],
            page: 1,
            pageSize: 10,
            totalCount: 1,
            totalPages: 1,
          }),
        } as Response;
      }

      if (url.includes('/api/auth/login')) {
        if (emailUnconfirmed) {
          return {
            ok: false,
            status: 401,
            json: async () => ({
              title: 'Email Not Confirmed',
              detail: 'Please confirm your email address before signing in.',
            }),
          } as Response;
        }

        if (loginRequiresTwoFactor) {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              message: 'Two-factor verification required',
              requiresTwoFactor: true,
              user: null,
              antiforgeryToken: 'challenge-csrf-token-123',
            }),
          } as Response;
        }

        return {
          ok: true,
          status: 200,
          json: async () => ({
            message: 'Sign-in successful',
            user: {
              id: '11111111-2222-3333-4444-555555555555',
              email: 'coordinator@campus.internal',
            },
            antiforgeryToken: 'refreshed-token-456',
          }),
        } as Response;
      }

      if (url.includes('/api/auth/register')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            message: 'If this email address is eligible, a confirmation link has been sent. Please check your inbox.',
          }),
        } as Response;
      }

      if (url.includes('/api/auth/confirm-email')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            succeeded: true,
            message: 'Email confirmed successfully. You can now sign in.',
          }),
        } as Response;
      }

      if (url.includes('/api/auth/forgot-password')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            message: 'If this email address is registered and eligible, a password reset link has been sent. Please check your inbox.',
          }),
        } as Response;
      }

      if (url.includes('/api/auth/reset-password')) {
        if (typeof input === 'object' && 'body' in input) {
          // Check if body contains invalid token
        }
        return {
          ok: true,
          status: 200,
          json: async () => ({
            message: 'Password has been successfully reset. You may now sign in with your new password.',
          }),
        } as Response;
      }

      if (url.includes('/api/auth/change-password')) {
        if (changePasswordError) {
          return {
            ok: false,
            status: 400,
            json: async () => ({
              title: 'Password Change Failed',
              detail: changePasswordError,
            }),
          } as Response;
        }

        return {
          ok: true,
          status: 200,
          json: async () => ({
            message: 'Password changed successfully. Please sign in with your new password.',
            antiforgeryToken: 'anonymous-token-999',
          }),
        } as Response;
      }

      if (url.includes('/api/auth/sign-out-everywhere')) {
        if (signOutEverywhereError) {
          return {
            ok: false,
            status: 400,
            json: async () => ({
              title: 'Session Revocation Failed',
              detail: signOutEverywhereError,
            }),
          } as Response;
        }

        return {
          ok: true,
          status: 200,
          json: async () => ({
            message: 'You have been signed out from all devices successfully.',
            antiforgeryToken: 'anonymous-token-999',
          }),
        } as Response;
      }

      if (url.includes('/api/dev/mailbox/clear')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ message: 'Development mailbox cleared' }),
        } as Response;
      }

      if (url.includes('/api/dev/mailbox')) {
        return {
          ok: true,
          status: 200,
          json: async () => mailboxMessages,
        } as Response;
      }

      if (url.includes('/api/auth/2fa/status')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            isTwoFactorEnabled: twoFactorEnabled,
            hasAuthenticator: twoFactorEnabled,
            recoveryCodesLeft: recoveryCodesLeft ?? (twoFactorEnabled ? 10 : 0),
          }),
        } as Response;
      }

      if (url.includes('/api/auth/2fa/setup')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            manualEntryKey: 'abcd efgh ijkl mnop',
            unformattedKey: 'abcdefghijklmnop',
            authenticatorUri: 'otpauth://totp/CampusVenue:coordinator@campus.internal?secret=abcdefghijklmnop&issuer=CampusVenue&digits=6',
            qrCodeSvg: '<svg data-testid="mock-qr-svg"></svg>',
            qrCodeDataUrl: 'data:image/svg+xml;utf8,<svg></svg>',
          }),
        } as Response;
      }

      if (url.includes('/api/auth/2fa/enable')) {
        if (enrollError) {
          return {
            ok: false,
            status: 400,
            json: async () => ({
              title: 'Validation Error',
              detail: enrollError,
            }),
          } as Response;
        }

        return {
          ok: true,
          status: 200,
          json: async () => ({
            message: 'Two-factor authentication has been successfully enabled.',
            recoveryCodes: [
              '11111-11111',
              '22222-22222',
              '33333-33333',
              '44444-44444',
              '55555-55555',
              '66666-66666',
              '77777-77777',
              '88888-88888',
              '99999-99999',
              '00000-00000',
            ],
          }),
        } as Response;
      }

      if (url.includes('/api/auth/2fa/disable')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            message: 'Two-factor authentication has been disabled.',
          }),
        } as Response;
      }

      if (url.includes('/api/auth/2fa/regenerate-recovery-codes')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            message: 'New recovery codes generated.',
            recoveryCodes: [
              'aaaaa-aaaaa',
              'bbbbb-bbbbb',
              'ccccc-ccccc',
              'ddddd-ddddd',
              'eeeee-eeeee',
              'fffff-fffff',
              'ggggg-ggggg',
              'hhhhh-hhhhh',
              'iiiii-iiiii',
              'jjjjj-jjjjj',
            ],
          }),
        } as Response;
      }

      if (url.includes('/api/auth/2fa/challenge')) {
        if (challengeError) {
          return {
            ok: false,
            status: 400,
            json: async () => ({
              title: 'Invalid Verification Code',
              detail: challengeError,
            }),
          } as Response;
        }

        return {
          ok: true,
          status: 200,
          json: async () => ({
            message: 'Sign-in successful',
            user: {
              id: '11111111-2222-3333-4444-555555555555',
              email: 'coordinator@campus.internal',
            },
            antiforgeryToken: 'refreshed-after-2fa-token',
          }),
        } as Response;
      }

      if (url.includes('/api/auth/logout')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            message: 'Sign-out successful',
            antiforgeryToken: 'anonymous-token-789',
          }),
        } as Response;
      }

      return {
        ok: false,
        status: 404,
        json: async () => ({ error: 'Not found' }),
      } as Response;
    });
  };

  it('renders application brand, Phase 1A banner, and system health status', async () => {
    setupFetchMock({ healthOk: true });

    render(<App />);

    expect(screen.getByText('CampusVenue')).toBeInTheDocument();
    expect(screen.getByText('Phase 1A Active')).toBeInTheDocument();

    await waitFor(() => {
      const healthyBadges = screen.getAllByText('Healthy');
      expect(healthyBadges.length).toBeGreaterThanOrEqual(1);
      expect(screen.getByText('Development')).toBeInTheDocument();
    });
  });

  it('renders error state when backend health check fails', async () => {
    setupFetchMock({ healthOk: false });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByRole('alert')).toBeInTheDocument();
      expect(screen.getByText(/Health Check Failed/i)).toBeInTheDocument();
      expect(screen.getByText(/Network connection refused/i)).toBeInTheDocument();
    });
  });

  it('switches to Sign In tab and displays accessible login form with Create account link', async () => {
    setupFetchMock({ healthOk: true, authenticated: false });

    render(<App />);

    const signInNav = screen.getByTestId('nav-auth-tab');
    fireEvent.click(signInNav);

    await waitFor(() => {
      expect(screen.getByText('Sign in to CampusVenue')).toBeInTheDocument();
      expect(screen.getByLabelText(/University Email/i)).toBeInTheDocument();
      expect(screen.getByLabelText(/^Password$/i)).toBeInTheDocument();
      expect(screen.getByTestId('create-account-link')).toBeInTheDocument();
    });
  });

  it('navigates from Sign In form to Register form and validates password checklist', async () => {
    setupFetchMock({ healthOk: true, authenticated: false });

    render(<App />);

    const signInNav = screen.getByTestId('nav-auth-tab');
    fireEvent.click(signInNav);

    await waitFor(() => {
      expect(screen.getByTestId('create-account-link')).toBeInTheDocument();
    });

    // Click "Create account"
    fireEvent.click(screen.getByTestId('create-account-link'));

    await waitFor(() => {
      expect(screen.getByText('Create your account')).toBeInTheDocument();
      expect(screen.getByLabelText(/University Email Address/i)).toBeInTheDocument();
      expect(screen.getByLabelText(/^Password/i)).toBeInTheDocument();
      expect(screen.getByLabelText(/Confirm Password/i, { selector: 'input' })).toBeInTheDocument();
      expect(screen.getByText('Password Requirements:')).toBeInTheDocument();
    });

    const emailInput = screen.getByLabelText(/University Email Address/i);
    const passwordInput = screen.getByLabelText(/^Password/i);
    const confirmInput = screen.getByLabelText(/Confirm Password/i, { selector: 'input' });

    fireEvent.change(emailInput, { target: { value: 'newstudent@campus.internal' } });
    fireEvent.change(passwordInput, { target: { value: 'StrongPass2026!' } });
    fireEvent.change(confirmInput, { target: { value: 'StrongPass2026!' } });

    const submitBtn = screen.getByTestId('register-submit-btn');
    expect(submitBtn).not.toBeDisabled();

    // Submit registration
    fireEvent.click(submitBtn);

    // Verify confirmation notice appears
    await waitFor(() => {
      expect(screen.getByText('Check your email')).toBeInTheDocument();
      expect(screen.getByText('newstudent@campus.internal')).toBeInTheDocument();
      expect(screen.getByText('Local Development Inbox')).toBeInTheDocument();
      expect(screen.getByTestId('open-dev-mailbox-btn')).toBeInTheDocument();
    });
  });

  it('switches to Dev Mailbox tab and displays captured emails with verification actions', async () => {
    const mockEmail = {
      id: '22222222-3333-4444-5555-666666666666',
      to: 'student@campus.internal',
      subject: 'Verify your CampusVenue account',
      bodyHtml: '<p>Welcome! <a href="http://localhost:3000/?tab=verify&userId=22222222-3333-4444-5555-666666666666&token=valid-test-token-123">Confirm Email Address</a></p>',
      sentAtUtc: new Date().toISOString(),
    };

    setupFetchMock({ healthOk: true, mailboxMessages: [mockEmail] });

    render(<App />);

    const mailboxTab = screen.getByTestId('nav-mailbox-tab');
    fireEvent.click(mailboxTab);

    await waitFor(() => {
      expect(screen.getByText('Development Mailbox')).toBeInTheDocument();
      expect(screen.getAllByText('student@campus.internal').length).toBeGreaterThanOrEqual(1);
      expect(screen.getAllByText('Verify your CampusVenue account').length).toBeGreaterThanOrEqual(1);
      expect(screen.getByTestId('one-click-verify-btn')).toBeInTheDocument();
    });

    // Click "Confirm Now in App"
    fireEvent.click(screen.getByTestId('one-click-verify-btn'));

    await waitFor(() => {
      expect(screen.getByText('Email Confirmed!')).toBeInTheDocument();
      expect(screen.getByTestId('verified-signin-btn')).toBeInTheDocument();
    });
  });

  it('displays email confirmation warning when user attempts login with unconfirmed email', async () => {
    setupFetchMock({ healthOk: true, authenticated: false, emailUnconfirmed: true });

    render(<App />);

    const signInNav = screen.getByTestId('nav-auth-tab');
    fireEvent.click(signInNav);

    await waitFor(() => {
      expect(screen.getByLabelText(/University Email/i)).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText(/University Email/i), { target: { value: 'unconfirmed@campus.internal' } });
    fireEvent.change(screen.getByLabelText(/^Password$/i), { target: { value: 'ValidPass123!' } });
    fireEvent.click(screen.getByTestId('login-submit-btn'));

    await waitFor(() => {
      expect(screen.getByText('Email Confirmation Required')).toBeInTheDocument();
      expect(screen.getByText(/Please confirm your email address before signing in/i)).toBeInTheDocument();
    });
  });

  it('restores authenticated session automatically when /api/auth/me returns valid user', async () => {
    setupFetchMock({ healthOk: true, authenticated: true });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('coordinator@campus.internal')).toBeInTheDocument();
      expect(screen.getByText('My Account')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('My Account'));

    await waitFor(() => {
      expect(screen.getByText('Authenticated')).toBeInTheDocument();
      expect(screen.getByText(/ID: 11111111-2222-3333-4444-555555555555/i)).toBeInTheDocument();
    });
  });

  it('signs out successfully and returns user to anonymous state', async () => {
    setupFetchMock({ healthOk: true, authenticated: true });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('coordinator@campus.internal')).toBeInTheDocument();
    });

    const signOutBtn = screen.getAllByRole('button', { name: /Sign Out/i })[0];
    fireEvent.click(signOutBtn);

    await waitFor(() => {
      const signInLabels = screen.getAllByText('Sign In');
      expect(signInLabels.length).toBeGreaterThanOrEqual(1);
      expect(screen.queryByText('coordinator@campus.internal')).not.toBeInTheDocument();
    });
  });

  it('navigates from Sign In to Forgot Password form and submits reset request', async () => {
    setupFetchMock({ healthOk: true, authenticated: false });

    render(<App />);

    const signInNav = screen.getByTestId('nav-auth-tab');
    fireEvent.click(signInNav);

    await waitFor(() => {
      expect(screen.getByTestId('forgot-password-link')).toBeInTheDocument();
    });

    // Click "Forgot password?"
    fireEvent.click(screen.getByTestId('forgot-password-link'));

    await waitFor(() => {
      expect(screen.getByText('Forgot your password?')).toBeInTheDocument();
      expect(screen.getByLabelText(/University Email Address/i)).toBeInTheDocument();
      expect(screen.getByTestId('forgot-password-submit-btn')).toBeInTheDocument();
    });

    const emailInput = screen.getByLabelText(/University Email Address/i);
    fireEvent.change(emailInput, { target: { value: 'recovery_student@campus.internal' } });

    fireEvent.click(screen.getByTestId('forgot-password-submit-btn'));

    await waitFor(() => {
      expect(screen.getByText('Check your email')).toBeInTheDocument();
      expect(screen.getByText('recovery_student@campus.internal')).toBeInTheDocument();
      expect(screen.getByTestId('forgot-password-mailbox-btn')).toBeInTheDocument();
      expect(screen.getByTestId('forgot-password-return-login-btn')).toBeInTheDocument();
    });
  });

  it('renders Reset Password form when loaded via direct link parameters and enforces password checklist', async () => {
    setupFetchMock({ healthOk: true, authenticated: false });

    // Using URL fragment keeping the token out of initial HTTP request to the server
    window.history.pushState({}, '', '/?tab=reset-password#userId=33333333-4444-5555-6666-777777777777&token=valid-reset-token');

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('Choose a new password')).toBeInTheDocument();
      expect(screen.getByLabelText(/^New Password/i)).toBeInTheDocument();
      expect(screen.getByLabelText(/Confirm New Password/i)).toBeInTheDocument();
      expect(screen.getByText('Password Requirements:')).toBeInTheDocument();
    });

    const newPasswordInput = screen.getByLabelText(/^New Password/i);
    const confirmPasswordInput = screen.getByLabelText(/Confirm New Password/i);
    const submitBtn = screen.getByTestId('reset-password-submit-btn');

    // Initially disabled
    expect(submitBtn).toBeDisabled();

    // Type weak password
    fireEvent.change(newPasswordInput, { target: { value: 'weak' } });
    fireEvent.change(confirmPasswordInput, { target: { value: 'weak' } });
    expect(submitBtn).toBeDisabled();

    // Type strong matching password
    fireEvent.change(newPasswordInput, { target: { value: 'BrandNewSecurePass123!' } });
    fireEvent.change(confirmPasswordInput, { target: { value: 'BrandNewSecurePass123!' } });
    expect(submitBtn).not.toBeDisabled();

    // Submit reset
    fireEvent.click(submitBtn);

    // Verify redirection to Sign In with success notice
    await waitFor(() => {
      expect(screen.getByTestId('login-success-banner')).toBeInTheDocument();
      expect(screen.getByText('Password Reset Completed')).toBeInTheDocument();
      expect(screen.getByText('Sign in to CampusVenue')).toBeInTheDocument();
    });
  });

  it('renders Invalid or Expired Link view when loaded with missing reset parameters', async () => {
    setupFetchMock({ healthOk: true, authenticated: false });

    window.history.pushState({}, '', '/?tab=reset-password#userId=&token=');

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('Invalid or Expired Link')).toBeInTheDocument();
      expect(screen.getByTestId('request-new-link-btn')).toBeInTheDocument();
    });

    // Clicking "Request New Reset Link" navigates to Forgot Password form
    fireEvent.click(screen.getByTestId('request-new-link-btn'));

    await waitFor(() => {
      expect(screen.getByText('Forgot your password?')).toBeInTheDocument();
    });
  });

  it('detects password reset link in DevMailbox and navigates to Reset Password form', async () => {
    const mockResetEmail = {
      id: '44444444-5555-6666-7777-888888888888',
      to: 'reset_user@campus.internal',
      subject: 'Reset your CampusVenue password',
      bodyHtml: '<p>Reset password: <a href="http://localhost:3000/?tab=reset-password#userId=44444444-5555-6666-7777-888888888888&token=reset-token-xyz">Reset Password</a></p>',
      sentAtUtc: new Date().toISOString(),
    };

    setupFetchMock({ healthOk: true, mailboxMessages: [mockResetEmail] });

    render(<App />);

    const mailboxTab = screen.getByTestId('nav-mailbox-tab');
    fireEvent.click(mailboxTab);

    await waitFor(() => {
      expect(screen.getByText('Development Mailbox')).toBeInTheDocument();
      expect(screen.getAllByText('reset_user@campus.internal').length).toBeGreaterThanOrEqual(1);
      expect(screen.getByTestId('one-click-reset-btn')).toBeInTheDocument();
    });

    // Click "Reset Password in App ->"
    fireEvent.click(screen.getByTestId('one-click-reset-btn'));

    await waitFor(() => {
      expect(screen.getByText('Choose a new password')).toBeInTheDocument();
      expect(screen.getByTestId('reset-password-submit-btn')).toBeInTheDocument();
    });
  });

  it('navigates to Account screen, toggles Change Password form, and verifies real-time password requirements', async () => {
    setupFetchMock({ healthOk: true, authenticated: true });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('My Account')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('My Account'));

    await waitFor(() => {
      expect(screen.getByText('Account Password')).toBeInTheDocument();
      expect(screen.getByTestId('open-change-password-btn')).toBeInTheDocument();
    });

    // Open change password form
    fireEvent.click(screen.getByTestId('open-change-password-btn'));

    await waitFor(() => {
      expect(screen.getByText('Change Account Password')).toBeInTheDocument();
      expect(screen.getByLabelText(/^Current Password/i)).toBeInTheDocument();
      expect(screen.getByLabelText(/^New Password/i)).toBeInTheDocument();
      expect(screen.getByLabelText(/Confirm New Password/i, { selector: 'input' })).toBeInTheDocument();
      expect(screen.getByText('New Password Requirements:')).toBeInTheDocument();
    });

    const submitBtn = screen.getByTestId('change-password-submit-btn');
    expect(submitBtn).toBeDisabled();

    // Type current password and weak new password
    fireEvent.change(screen.getByLabelText(/^Current Password/i), { target: { value: 'OldPassword123!' } });
    fireEvent.change(screen.getByLabelText(/^New Password/i), { target: { value: 'weak' } });
    fireEvent.change(screen.getByLabelText(/Confirm New Password/i, { selector: 'input' }), { target: { value: 'weak' } });

    expect(submitBtn).toBeDisabled();

    // Type strong new password
    fireEvent.change(screen.getByLabelText(/^New Password/i), { target: { value: 'NewSecurePass2026!' } });
    fireEvent.change(screen.getByLabelText(/Confirm New Password/i, { selector: 'input' }), { target: { value: 'NewSecurePass2026!' } });

    expect(submitBtn).not.toBeDisabled();

    // Click cancel to return
    fireEvent.click(screen.getByTestId('change-password-cancel-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('open-change-password-btn')).toBeInTheDocument();
      expect(screen.queryByText('Change Account Password')).not.toBeInTheDocument();
    });
  });

  it('submits valid password change from Account screen, clears session, redirects to Sign In, and shows success notice', async () => {
    setupFetchMock({ healthOk: true, authenticated: true });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('My Account')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('My Account'));

    await waitFor(() => {
      expect(screen.getByTestId('open-change-password-btn')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('open-change-password-btn'));

    await waitFor(() => {
      expect(screen.getByText('Change Account Password')).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText(/^Current Password/i), { target: { value: 'OldPassword123!' } });
    fireEvent.change(screen.getByLabelText(/^New Password/i), { target: { value: 'NewSecurePass2026!' } });
    fireEvent.change(screen.getByLabelText(/Confirm New Password/i, { selector: 'input' }), { target: { value: 'NewSecurePass2026!' } });

    const submitBtn = screen.getByTestId('change-password-submit-btn');
    expect(submitBtn).not.toBeDisabled();

    fireEvent.click(submitBtn);

    // Verify user is redirected to Sign In and success banner appears
    await waitFor(() => {
      expect(screen.getByTestId('login-success-banner')).toBeInTheDocument();
      expect(screen.getByText('Password Changed Successfully')).toBeInTheDocument();
      expect(screen.getByText(/Password changed successfully. Please sign in with your new password./i)).toBeInTheDocument();
      expect(screen.getByText('Sign in to CampusVenue')).toBeInTheDocument();
    });
  });

  it('displays error banner when changing password fails with incorrect current password', async () => {
    setupFetchMock({
      healthOk: true,
      authenticated: true,
      changePasswordError: 'The current password provided is incorrect.',
    });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('My Account')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('My Account'));

    await waitFor(() => {
      expect(screen.getByTestId('open-change-password-btn')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('open-change-password-btn'));

    await waitFor(() => {
      expect(screen.getByText('Change Account Password')).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText(/^Current Password/i), { target: { value: 'WrongCurrentPassword!' } });
    fireEvent.change(screen.getByLabelText(/^New Password/i), { target: { value: 'NewSecurePass2026!' } });
    fireEvent.change(screen.getByLabelText(/Confirm New Password/i, { selector: 'input' }), { target: { value: 'NewSecurePass2026!' } });

    fireEvent.click(screen.getByTestId('change-password-submit-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('change-password-error-banner')).toBeInTheDocument();
      expect(screen.getByText('The current password provided is incorrect.')).toBeInTheDocument();
    });
  });

  it('navigates to Account screen, opens Sign out everywhere dialog, and cancels without signing out', async () => {
    setupFetchMock({ healthOk: true, authenticated: true });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('My Account')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('My Account'));

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: 'Sign Out Everywhere' })).toBeInTheDocument();
      expect(screen.getByTestId('open-sign-out-everywhere-btn')).toBeInTheDocument();
    });

    // Open confirmation dialog
    fireEvent.click(screen.getByTestId('open-sign-out-everywhere-btn'));

    await waitFor(() => {
      expect(screen.getByText('Sign out from all devices?')).toBeInTheDocument();
      expect(screen.getByText(/Other active sessions may take up to 5 minutes to be rejected/i)).toBeInTheDocument();
      expect(screen.getByTestId('confirm-sign-out-everywhere-btn')).toBeInTheDocument();
      expect(screen.getByTestId('cancel-sign-out-everywhere-btn')).toBeInTheDocument();
    });

    // Click cancel
    fireEvent.click(screen.getByTestId('cancel-sign-out-everywhere-btn'));

    await waitFor(() => {
      expect(screen.queryByText('Sign out from all devices?')).not.toBeInTheDocument();
      expect(screen.getAllByText('coordinator@campus.internal').length).toBeGreaterThanOrEqual(1);
    });
  });

  it('confirms Sign out everywhere, terminates session, redirects to Sign In, and shows confirmation banner', async () => {
    setupFetchMock({ healthOk: true, authenticated: true });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('My Account')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('My Account'));

    await waitFor(() => {
      expect(screen.getByTestId('open-sign-out-everywhere-btn')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('open-sign-out-everywhere-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('confirm-sign-out-everywhere-btn')).toBeInTheDocument();
    });

    // Confirm sign out everywhere
    fireEvent.click(screen.getByTestId('confirm-sign-out-everywhere-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('login-success-banner')).toBeInTheDocument();
      expect(screen.getByText('Signed out on this device')).toBeInTheDocument();
      expect(screen.getByText(/You have been signed out from all devices successfully./i)).toBeInTheDocument();
      expect(screen.getByText(/Other devices may continue to have access for up to five minutes/i)).toBeInTheDocument();
      expect(screen.getByText('Sign in to CampusVenue')).toBeInTheDocument();
    });
  });

  it('displays error banner when Sign out everywhere fails', async () => {
    setupFetchMock({
      healthOk: true,
      authenticated: true,
      signOutEverywhereError: 'Database connection failed during stamp rotation.',
    });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('My Account')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('My Account'));

    await waitFor(() => {
      expect(screen.getByTestId('open-sign-out-everywhere-btn')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('open-sign-out-everywhere-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('confirm-sign-out-everywhere-btn')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('confirm-sign-out-everywhere-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('soe-error-banner')).toBeInTheDocument();
      expect(screen.getByText('Database connection failed during stamp rotation.')).toBeInTheDocument();
    });
  });

  it('renders Two-Factor section on Account screen with Disabled badge when not enrolled', async () => {
    setupFetchMock({
      healthOk: true,
      authenticated: true,
      twoFactorEnabled: false,
    });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('My Account')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('My Account'));

    await waitFor(() => {
      expect(screen.getByText('Two-Factor Authentication')).toBeInTheDocument();
      expect(screen.getByTestId('mfa-status-badge')).toHaveTextContent('Disabled');
      expect(screen.getByTestId('open-enable-2fa-btn')).toBeInTheDocument();
    });
  });

  it('completes Two-Factor enrollment flow, shows local QR code and setup key, and displays 10 recovery codes', async () => {
    setupFetchMock({
      healthOk: true,
      authenticated: true,
      twoFactorEnabled: false,
    });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('My Account')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('My Account'));

    await waitFor(() => {
      expect(screen.getByTestId('open-enable-2fa-btn')).toBeInTheDocument();
    });

    // Open enrollment modal
    fireEvent.click(screen.getByTestId('open-enable-2fa-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('mfa-qr-code')).toBeInTheDocument();
      expect(screen.getByTestId('mfa-manual-key')).toHaveTextContent('abcd efgh ijkl mnop');
      expect(screen.getByTestId('mfa-current-password-input')).toBeInTheDocument();
      expect(screen.getByTestId('mfa-enroll-code-input')).toBeInTheDocument();
    });

    // Enter credentials and code
    fireEvent.change(screen.getByTestId('mfa-current-password-input'), {
      target: { value: 'CurrentPassword123!' },
    });
    fireEvent.change(screen.getByTestId('mfa-enroll-code-input'), {
      target: { value: '123456' },
    });

    // Submit enrollment
    fireEvent.click(screen.getByTestId('submit-enable-2fa-btn'));

    // Should display recovery codes modal with 10 codes
    await waitFor(() => {
      expect(screen.getByText('Save Your Recovery Codes')).toBeInTheDocument();
      expect(screen.getByTestId('recovery-codes-list')).toBeInTheDocument();
      expect(screen.getByText('11111-11111')).toBeInTheDocument();
      expect(screen.getByTestId('copy-recovery-codes-btn')).toBeInTheDocument();
      expect(screen.getByTestId('confirm-saved-recovery-codes-btn')).toBeInTheDocument();
    });

    // Confirm saved recovery codes to dismiss
    fireEvent.click(screen.getByTestId('confirm-saved-recovery-codes-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('mfa-success-banner')).toBeInTheDocument();
      expect(screen.getByText(/Two-Factor Authentication is now enabled/i)).toBeInTheDocument();
    });
  });

  it('renders Two-Factor section as Active with recovery codes count when enabled', async () => {
    setupFetchMock({
      healthOk: true,
      authenticated: true,
      twoFactorEnabled: true,
      recoveryCodesLeft: 8,
    });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('My Account')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('My Account'));

    await waitFor(() => {
      expect(screen.getByText('Two-Factor Authentication')).toBeInTheDocument();
      expect(screen.getByTestId('mfa-status-badge')).toHaveTextContent('Active');
      expect(screen.getByText(/You have 8 recovery codes remaining/i)).toBeInTheDocument();
      expect(screen.getByTestId('open-regenerate-codes-btn')).toBeInTheDocument();
      expect(screen.getByTestId('open-disable-2fa-btn')).toBeInTheDocument();
    });
  });

  it('regenerates recovery codes from Account screen with password and code verification', async () => {
    setupFetchMock({
      healthOk: true,
      authenticated: true,
      twoFactorEnabled: true,
      recoveryCodesLeft: 5,
    });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('My Account')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('My Account'));

    await waitFor(() => {
      expect(screen.getByTestId('open-regenerate-codes-btn')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('open-regenerate-codes-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('regen-current-password-input')).toBeInTheDocument();
      expect(screen.getByTestId('regen-code-input')).toBeInTheDocument();
    });

    fireEvent.change(screen.getByTestId('regen-current-password-input'), {
      target: { value: 'CurrentPassword123!' },
    });
    fireEvent.change(screen.getByTestId('regen-code-input'), {
      target: { value: '654321' },
    });

    fireEvent.click(screen.getByTestId('submit-regen-codes-btn'));

    await waitFor(() => {
      expect(screen.getByText('Save Your Recovery Codes')).toBeInTheDocument();
      expect(screen.getByText('aaaaa-aaaaa')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('confirm-saved-recovery-codes-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('mfa-success-banner')).toBeInTheDocument();
      expect(screen.getByText(/New recovery codes generated/i)).toBeInTheDocument();
    });
  });

  it('disables Two-Factor authentication from Account screen with password and code verification', async () => {
    setupFetchMock({
      healthOk: true,
      authenticated: true,
      twoFactorEnabled: true,
      recoveryCodesLeft: 10,
    });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('My Account')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('My Account'));

    await waitFor(() => {
      expect(screen.getByTestId('open-disable-2fa-btn')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('open-disable-2fa-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('disable-current-password-input')).toBeInTheDocument();
      expect(screen.getByTestId('disable-code-input')).toBeInTheDocument();
    });

    fireEvent.change(screen.getByTestId('disable-current-password-input'), {
      target: { value: 'CurrentPassword123!' },
    });
    fireEvent.change(screen.getByTestId('disable-code-input'), {
      target: { value: '654321' },
    });

    fireEvent.click(screen.getByTestId('submit-disable-2fa-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('mfa-success-banner')).toBeInTheDocument();
      expect(screen.getByText(/Two-Factor Authentication has been disabled/i)).toBeInTheDocument();
    });
  });

  it('handles MFA challenge during sign-in, supports toggle to recovery code, and signs in on valid code', async () => {
    setupFetchMock({
      healthOk: true,
      authenticated: false,
      loginRequiresTwoFactor: true,
    });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByTestId('nav-auth-tab')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('nav-auth-tab'));

    await waitFor(() => {
      expect(screen.getByLabelText(/university email/i)).toBeInTheDocument();
    });

    // Enter credentials
    fireEvent.change(screen.getByLabelText(/university email/i), {
      target: { value: 'mfa.user@campus.internal' },
    });
    fireEvent.change(screen.getByLabelText(/^password/i), {
      target: { value: 'Password123!' },
    });

    fireEvent.click(screen.getByTestId('login-submit-btn'));

    // Should switch to Two-Factor challenge screen
    await waitFor(() => {
      expect(screen.getByText('Two-Factor Authentication')).toBeInTheDocument();
      expect(screen.getByTestId('mfa-code-input')).toBeInTheDocument();
      expect(screen.getByTestId('switch-to-recovery-code-btn')).toBeInTheDocument();
    });

    // Switch to recovery code mode
    fireEvent.click(screen.getByTestId('switch-to-recovery-code-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('recovery-code-input')).toBeInTheDocument();
      expect(screen.getByTestId('switch-to-authenticator-btn')).toBeInTheDocument();
    });

    // Switch back to authenticator mode
    fireEvent.click(screen.getByTestId('switch-to-authenticator-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('mfa-code-input')).toBeInTheDocument();
    });

    // Enter verification code and submit
    fireEvent.change(screen.getByTestId('mfa-code-input'), {
      target: { value: '123456' },
    });

    fireEvent.click(screen.getByTestId('submit-mfa-challenge-btn'));

    // Should successfully authenticate and navigate to Account screen
    await waitFor(() => {
      expect(screen.getAllByText('coordinator@campus.internal').length).toBeGreaterThan(0);
      expect(screen.getAllByRole('button', { name: /Sign Out/i }).length).toBeGreaterThan(0);
    });
  });

  it('does not display Users nav tab for standard participant or admin without MFA session', async () => {
    setupFetchMock({
      healthOk: true,
      authenticated: true,
      roles: ['Participant'],
      mfaCompleted: false,
    });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByTestId('nav-overview-tab')).toBeInTheDocument();
      expect(screen.getByTestId('nav-auth-tab')).toBeInTheDocument();
    });

    expect(screen.queryByTestId('nav-users-tab')).not.toBeInTheDocument();
  });

  it('displays Users navigation tab and navigates to directory when session has UniversityAdmin and completed MFA', async () => {
    setupFetchMock({
      healthOk: true,
      authenticated: true,
      roles: ['UniversityAdmin'],
      mfaCompleted: true,
      twoFactorEnabled: true,
    });

    render(<App />);

    await waitFor(() => {
      expect(screen.getByTestId('nav-users-tab')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('nav-users-tab'));

    await waitFor(() => {
      expect(screen.getByText('Institutional User Directory')).toBeInTheDocument();
      expect(screen.getByTestId('users-table')).toBeInTheDocument();
      expect(screen.getByText('admin@campus.internal')).toBeInTheDocument();
    });
  });
});
