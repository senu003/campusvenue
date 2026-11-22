import type {
  User,
  LoginCredentials,
  RegisterCredentials,
  LoginResponseDto,
  LoginResult,
  CurrentUserResponseDto,
  LogoutResponseDto,
  AntiforgeryResponseDto,
  RegisterResponseDto,
  ConfirmEmailResponseDto,
  ForgotPasswordCredentials,
  ForgotPasswordResponseDto,
  ResetPasswordCredentials,
  ResetPasswordResponseDto,
  ChangePasswordCredentials,
  ChangePasswordResponseDto,
  SignOutEverywhereResponseDto,
  DevEmailDto,
  TwoFactorStatusDto,
  TwoFactorSetupDto,
  EnableTwoFactorRequestDto,
  EnableTwoFactorResponseDto,
  DisableTwoFactorRequestDto,
  DisableTwoFactorResponseDto,
  RegenerateRecoveryCodesRequestDto,
  RegenerateRecoveryCodesResponseDto,
  TwoFactorChallengeRequestDto,
} from './types';

// In-memory Antiforgery token cache. Never stored in localStorage or sessionStorage.
let memoryAntiforgeryToken: string | null = null;

export const setCachedAntiforgeryToken = (token: string | null): void => {
  memoryAntiforgeryToken = token;
};

export const getCachedAntiforgeryToken = (): string | null => {
  return memoryAntiforgeryToken;
};

export async function fetchAntiforgeryToken(): Promise<string> {
  const response = await fetch('/api/auth/antiforgery', {
    method: 'GET',
    headers: {
      'Accept': 'application/json',
    },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to obtain security token: HTTP ${response.status}`);
  }

  const data: AntiforgeryResponseDto = await response.json();
  memoryAntiforgeryToken = data.requestToken;
  return data.requestToken;
}

export async function getCurrentUser(): Promise<User | null> {
  const response = await fetch('/api/auth/me', {
    method: 'GET',
    headers: {
      'Accept': 'application/json',
    },
    credentials: 'include',
  });

  if (response.status === 401) {
    return null;
  }

  if (!response.ok) {
    throw new Error(`Error verifying session: HTTP ${response.status}`);
  }

  const data: CurrentUserResponseDto = await response.json();
  return {
    id: data.id,
    email: data.email,
    twoFactorEnabled: data.twoFactorEnabled ?? false,
    roles: data.roles ?? ['Participant'],
    mfaCompleted: data.mfaCompleted ?? false,
  };
}

export class AuthError extends Error {
  isEmailUnconfirmed: boolean;

  constructor(message: string, isEmailUnconfirmed = false) {
    super(message);
    this.name = 'AuthError';
    this.isEmailUnconfirmed = isEmailUnconfirmed;
  }
}

export async function loginUser(credentials: LoginCredentials): Promise<LoginResult> {
  let token = memoryAntiforgeryToken;
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch('/api/auth/login', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify({
      email: credentials.email,
      password: credentials.password,
      rememberMe: credentials.rememberMe ?? false,
    }),
  });

  if (response.status === 401) {
    const errorData = await response.json().catch(() => null);
    const title = (errorData && typeof errorData === 'object' && 'title' in errorData && typeof errorData.title === 'string')
      ? errorData.title
      : '';
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'Invalid email or password.';

    const isUnconfirmed = title === 'Email Not Confirmed' || detail.toLowerCase().includes('confirm your email');
    throw new AuthError(detail, isUnconfirmed);
  }

  if (response.status === 429) {
    throw new AuthError('Too many sign-in attempts. Please wait a minute before trying again.');
  }

  if (response.status === 400) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'Request validation or security check failed.';
    // If antiforgery failed, clear token so next attempt re-fetches
    memoryAntiforgeryToken = null;
    throw new AuthError(detail);
  }

  if (!response.ok) {
    throw new AuthError(`Sign-in failed: Server returned HTTP ${response.status}`);
  }

  const data: LoginResponseDto = await response.json();
  if (data.antiforgeryToken) {
    memoryAntiforgeryToken = data.antiforgeryToken;
  }

  if (data.requiresTwoFactor) {
    return {
      success: false,
      requiresTwoFactor: true,
      user: null,
    };
  }

  return {
    success: true,
    requiresTwoFactor: false,
    user: {
      id: data.user?.id ?? '',
      email: data.user?.email ?? '',
      twoFactorEnabled: false,
    },
  };
}

export async function registerUser(credentials: RegisterCredentials): Promise<RegisterResponseDto> {
  let token = memoryAntiforgeryToken;
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch('/api/auth/register', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify({
      email: credentials.email,
      password: credentials.password,
      confirmPassword: credentials.confirmPassword,
    }),
  });

  if (response.status === 429) {
    throw new Error('Too many registration requests. Please wait a minute before trying again.');
  }

  if (response.status === 400) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'Registration validation failed.';
    throw new Error(detail);
  }

  if (!response.ok) {
    throw new Error(`Registration failed: Server returned HTTP ${response.status}`);
  }

  const data: RegisterResponseDto = await response.json();
  return data;
}

export async function confirmEmail(userId: string, token: string): Promise<ConfirmEmailResponseDto> {
  const response = await fetch('/api/auth/confirm-email', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    },
    body: JSON.stringify({
      userId,
      token,
    }),
  });

  if (!response.ok) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : `Email confirmation failed with HTTP ${response.status}`;
    throw new Error(detail);
  }

  const data: ConfirmEmailResponseDto = await response.json();
  return data;
}

export async function logoutUser(): Promise<void> {
  let token = memoryAntiforgeryToken;
  if (!token) {
    token = await fetchAntiforgeryToken().catch(() => null);
  }

  const headers: Record<string, string> = {
    'Accept': 'application/json',
  };
  if (token) {
    headers['X-XSRF-TOKEN'] = token;
  }

  const response = await fetch('/api/auth/logout', {
    method: 'POST',
    headers,
    credentials: 'include',
  });

  if (response.ok) {
    const data: LogoutResponseDto = await response.json().catch(() => null);
    if (data && data.antiforgeryToken) {
      memoryAntiforgeryToken = data.antiforgeryToken;
    }
  } else {
    // If logout failed, refresh token for safety
    memoryAntiforgeryToken = null;
    throw new Error(`Logout failed with HTTP ${response.status}`);
  }
}

export async function fetchDevMailbox(): Promise<DevEmailDto[]> {
  const response = await fetch('/api/dev/mailbox', {
    headers: {
      'Accept': 'application/json',
    },
  });

  if (!response.ok) {
    throw new Error(`Failed to load development mailbox: HTTP ${response.status}`);
  }

  const data: DevEmailDto[] = await response.json();
  return data;
}

export async function forgotPassword(credentials: ForgotPasswordCredentials): Promise<ForgotPasswordResponseDto> {
  let token = memoryAntiforgeryToken;
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch('/api/auth/forgot-password', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify({
      email: credentials.email,
    }),
  });

  if (response.status === 429) {
    throw new Error('Too many password reset requests. Please wait a minute before trying again.');
  }

  if (response.status === 400) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'Password recovery request validation failed.';
    throw new Error(detail);
  }

  if (!response.ok) {
    throw new Error(`Password reset request failed: Server returned HTTP ${response.status}`);
  }

  const data: ForgotPasswordResponseDto = await response.json();
  return data;
}

export async function resetPassword(credentials: ResetPasswordCredentials): Promise<ResetPasswordResponseDto> {
  let token = memoryAntiforgeryToken;
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch('/api/auth/reset-password', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify({
      userId: credentials.userId,
      token: credentials.token,
      newPassword: credentials.newPassword,
      confirmPassword: credentials.confirmPassword,
    }),
  });

  if (response.status === 429) {
    throw new Error('Too many password reset attempts. Please wait a minute before trying again.');
  }

  if (response.status === 400) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'Password reset validation failed.';
    throw new Error(detail);
  }

  if (!response.ok) {
    throw new Error(`Password reset failed: Server returned HTTP ${response.status}`);
  }

  const data: ResetPasswordResponseDto = await response.json();
  return data;
}

export async function changePassword(credentials: ChangePasswordCredentials): Promise<ChangePasswordResponseDto> {
  let token = memoryAntiforgeryToken;
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch('/api/auth/change-password', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify({
      currentPassword: credentials.currentPassword,
      newPassword: credentials.newPassword,
      confirmPassword: credentials.confirmPassword,
    }),
  });

  if (response.status === 401) {
    throw new Error('Your session has expired or is unauthorized. Please sign in again.');
  }

  if (response.status === 429) {
    throw new Error('Too many password change attempts. Please wait a minute before trying again.');
  }

  if (response.status === 400) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'Password change validation failed.';
    if (detail.toLowerCase().includes('antiforgery')) {
      memoryAntiforgeryToken = null;
    }
    throw new Error(detail);
  }

  if (!response.ok) {
    throw new Error(`Password change failed: Server returned HTTP ${response.status}`);
  }

  const data: ChangePasswordResponseDto = await response.json();
  if (data.antiforgeryToken) {
    memoryAntiforgeryToken = data.antiforgeryToken;
  }

  return data;
}

export async function signOutEverywhere(): Promise<SignOutEverywhereResponseDto> {
  let token = memoryAntiforgeryToken;
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch('/api/auth/sign-out-everywhere', {
    method: 'POST',
    headers: {
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
  });

  if (response.status === 401) {
    throw new Error('Your session has expired or is unauthorized. Please sign in again.');
  }

  if (response.status === 429) {
    throw new Error('Too many session revocation requests. Please wait a minute before trying again.');
  }

  if (response.status === 400) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'Session revocation request validation failed.';
    if (detail.toLowerCase().includes('antiforgery')) {
      memoryAntiforgeryToken = null;
    }
    throw new Error(detail);
  }

  if (!response.ok) {
    throw new Error(`Failed to sign out from all devices: Server returned HTTP ${response.status}`);
  }

  const data: SignOutEverywhereResponseDto = await response.json();
  if (data.antiforgeryToken) {
    memoryAntiforgeryToken = data.antiforgeryToken;
  }

  return data;
}

export async function clearDevMailbox(): Promise<void> {
  const response = await fetch('/api/dev/mailbox/clear', {
    method: 'POST',
  });

  if (!response.ok) {
    throw new Error(`Failed to clear development mailbox: HTTP ${response.status}`);
  }
}

export async function verifyTwoFactorChallenge(params: TwoFactorChallengeRequestDto): Promise<User> {
  let token = memoryAntiforgeryToken;
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch('/api/auth/2fa/challenge', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify({
      code: params.code,
      isRecoveryCode: params.isRecoveryCode ?? false,
      rememberMe: params.rememberMe ?? false,
    }),
  });

  if (response.status === 401) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'Account is temporarily locked out. Please try again later.';
    throw new AuthError(detail);
  }

  if (response.status === 429) {
    throw new AuthError('Too many verification attempts. Please wait a minute before trying again.');
  }

  if (response.status === 400) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'Invalid verification code.';
    if (detail.toLowerCase().includes('antiforgery')) {
      memoryAntiforgeryToken = null;
    }
    throw new AuthError(detail);
  }

  if (!response.ok) {
    throw new AuthError(`Verification failed: Server returned HTTP ${response.status}`);
  }

  const data: LoginResponseDto = await response.json();
  if (data.antiforgeryToken) {
    memoryAntiforgeryToken = data.antiforgeryToken;
  }

  try {
    const currentUser = await getCurrentUser();
    if (currentUser) {
      return currentUser;
    }
  } catch {
    // Fallback if /me request fails
  }

  return {
    id: data.user?.id ?? '',
    email: data.user?.email ?? '',
    twoFactorEnabled: true,
    mfaCompleted: true,
  };
}

export async function getTwoFactorStatus(): Promise<TwoFactorStatusDto> {
  const response = await fetch('/api/auth/2fa/status', {
    method: 'GET',
    headers: {
      'Accept': 'application/json',
    },
    credentials: 'include',
  });

  if (response.status === 401) {
    throw new Error('Your session has expired. Please sign in again.');
  }

  if (!response.ok) {
    throw new Error(`Failed to check two-factor status: HTTP ${response.status}`);
  }

  return response.json();
}

export async function getTwoFactorSetup(): Promise<TwoFactorSetupDto> {
  const response = await fetch('/api/auth/2fa/setup', {
    method: 'GET',
    headers: {
      'Accept': 'application/json',
    },
    credentials: 'include',
  });

  if (response.status === 401) {
    throw new Error('Your session has expired. Please sign in again.');
  }

  if (response.status === 400) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'Two-factor authentication is already enabled.';
    throw new Error(detail);
  }

  if (!response.ok) {
    throw new Error(`Failed to load two-factor setup: HTTP ${response.status}`);
  }

  return response.json();
}

export async function enableTwoFactor(params: EnableTwoFactorRequestDto): Promise<EnableTwoFactorResponseDto> {
  let token = memoryAntiforgeryToken;
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch('/api/auth/2fa/enable', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify(params),
  });

  if (response.status === 401) {
    throw new Error('Your session has expired. Please sign in again.');
  }

  if (response.status === 429) {
    throw new Error('Too many requests. Please wait a minute before trying again.');
  }

  if (response.status === 400) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'Failed to enable two-factor authentication. Please check your password and code.';
    if (detail.toLowerCase().includes('antiforgery')) {
      memoryAntiforgeryToken = null;
    }
    throw new Error(detail);
  }

  if (!response.ok) {
    throw new Error(`Failed to enable two-factor authentication: HTTP ${response.status}`);
  }

  return response.json();
}

export async function disableTwoFactor(params: DisableTwoFactorRequestDto): Promise<DisableTwoFactorResponseDto> {
  let token = memoryAntiforgeryToken;
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch('/api/auth/2fa/disable', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify(params),
  });

  if (response.status === 401) {
    throw new Error('Your session has expired. Please sign in again.');
  }

  if (response.status === 429) {
    throw new Error('Too many requests. Please wait a minute before trying again.');
  }

  if (response.status === 400) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'Failed to disable two-factor authentication. Please check your password and code.';
    if (detail.toLowerCase().includes('antiforgery')) {
      memoryAntiforgeryToken = null;
    }
    throw new Error(detail);
  }

  if (!response.ok) {
    throw new Error(`Failed to disable two-factor authentication: HTTP ${response.status}`);
  }

  return response.json();
}

export async function regenerateRecoveryCodes(params: RegenerateRecoveryCodesRequestDto): Promise<RegenerateRecoveryCodesResponseDto> {
  let token = memoryAntiforgeryToken;
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch('/api/auth/2fa/regenerate-recovery-codes', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify(params),
  });

  if (response.status === 401) {
    throw new Error('Your session has expired. Please sign in again.');
  }

  if (response.status === 429) {
    throw new Error('Too many requests. Please wait a minute before trying again.');
  }

  if (response.status === 400) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'Failed to regenerate recovery codes. Please check your password and code.';
    if (detail.toLowerCase().includes('antiforgery')) {
      memoryAntiforgeryToken = null;
    }
    throw new Error(detail);
  }

  if (!response.ok) {
    throw new Error(`Failed to regenerate recovery codes: HTTP ${response.status}`);
  }

  return response.json();
}
