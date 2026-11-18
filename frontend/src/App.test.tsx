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
    emailUnconfirmed?: boolean;
    mailboxMessages?: Array<{ id: string; to: string; subject: string; bodyHtml: string; sentAtUtc: string }>;
  }) => {
    const {
      healthOk = true,
      authenticated = false,
      emailUnconfirmed = false,
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
            }),
          } as Response;
        }
        return {
          ok: false,
          status: 401,
          json: async () => ({ title: 'Unauthorized' }),
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

  it('renders application brand, Phase 1A.2 banner, and system health status', async () => {
    setupFetchMock({ healthOk: true });

    render(<App />);

    expect(screen.getByText('CampusVenue')).toBeInTheDocument();
    expect(screen.getByText('Phase 1A.2 Active')).toBeInTheDocument();

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
});
