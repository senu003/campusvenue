import { createContext } from 'react';
import type { User, LoginCredentials } from './types';

export interface AuthContextValue {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  error: string | null;
  pendingTwoFactor: boolean;
  login: (credentials: LoginCredentials) => Promise<{ success: boolean; requiresTwoFactor?: boolean }>;
  verifyChallenge: (code: string, isRecoveryCode?: boolean, rememberMe?: boolean) => Promise<boolean>;
  cancelTwoFactorChallenge: () => void;
  logout: () => Promise<void>;
  checkSession: () => Promise<void>;
  clearSession: () => void;
  clearError: () => void;
}

export const AuthContext = createContext<AuthContextValue | undefined>(undefined);
