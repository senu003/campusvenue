export interface User {
  id: string;
  email: string;
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
  user: UserDto;
  antiforgeryToken: string;
}

export interface CurrentUserResponseDto {
  id: string;
  email: string;
  isAuthenticated: boolean;
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

export interface DevEmailDto {
  id: string;
  to: string;
  subject: string;
  bodyHtml: string;
  sentAtUtc: string;
}
