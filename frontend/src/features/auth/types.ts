export interface User {
  id: string;
  email: string;
  twoFactorEnabled?: boolean;
  roles?: string[];
  mfaCompleted?: boolean;
}

export interface LoginCredentials {
  email: string;
  password: string;
  rememberMe?: boolean;
}

export interface RegisterCredentials {
  email: string;
  password: string;
  confirmPassword: string;
}

export interface UserDto {
  id: string;
  email: string;
}

export interface LoginResponseDto {
  message: string;
  user?: UserDto | null;
  antiforgeryToken: string;
  requiresTwoFactor?: boolean;
}

export interface CurrentUserResponseDto {
  id: string;
  email: string;
  isAuthenticated: boolean;
  twoFactorEnabled?: boolean;
  roles?: string[];
  mfaCompleted?: boolean;
}

export interface LogoutResponseDto {
  message: string;
  antiforgeryToken: string;
}

export interface AntiforgeryResponseDto {
  requestToken: string;
}

export interface RegisterResponseDto {
  message: string;
}

export interface ConfirmEmailRequestDto {
  userId: string;
  token: string;
}

export interface ConfirmEmailResponseDto {
  succeeded: boolean;
  message: string;
}

export interface ForgotPasswordCredentials {
  email: string;
}

export interface ForgotPasswordResponseDto {
  message: string;
}

export interface ResetPasswordCredentials {
  userId: string;
  token: string;
  newPassword: string;
  confirmPassword: string;
}

export interface ResetPasswordResponseDto {
  message: string;
}

export interface ChangePasswordCredentials {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

export interface ChangePasswordResponseDto {
  message: string;
  antiforgeryToken?: string;
}

export interface SignOutEverywhereResponseDto {
  message: string;
  antiforgeryToken?: string;
}

export interface DevEmailDto {
  id: string;
  to: string;
  subject: string;
  bodyHtml: string;
  sentAtUtc: string;
}

export interface TwoFactorStatusDto {
  isTwoFactorEnabled: boolean;
  hasAuthenticator: boolean;
  recoveryCodesLeft: number;
}

export interface TwoFactorSetupDto {
  manualEntryKey: string;
  unformattedKey: string;
  authenticatorUri: string;
  qrCodeSvg: string;
  qrCodeDataUrl: string;
}

export interface EnableTwoFactorRequestDto {
  currentPassword: string;
  code: string;
}

export interface EnableTwoFactorResponseDto {
  message: string;
  recoveryCodes: string[];
}

export interface DisableTwoFactorRequestDto {
  currentPassword: string;
  code: string;
}

export interface DisableTwoFactorResponseDto {
  message: string;
}

export interface RegenerateRecoveryCodesRequestDto {
  currentPassword: string;
  code: string;
}

export interface RegenerateRecoveryCodesResponseDto {
  message: string;
  recoveryCodes: string[];
}

export interface TwoFactorChallengeRequestDto {
  code: string;
  isRecoveryCode?: boolean;
  rememberMe?: boolean;
}

export interface LoginResult {
  success: boolean;
  requiresTwoFactor?: boolean;
  user?: User | null;
}
