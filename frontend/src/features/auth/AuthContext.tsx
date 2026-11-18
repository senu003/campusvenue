import React, { useState, useEffect, useCallback, type ReactNode } from 'react';
import type { User, LoginCredentials } from './types';
import { getCurrentUser, loginUser, logoutUser, verifyTwoFactorChallenge, fetchAntiforgeryToken } from './api';
import { AuthContext, type AuthContextValue } from './context';

export const AuthProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [pendingTwoFactor, setPendingTwoFactor] = useState<boolean>(false);

  // Restore session from authoritative /api/auth/me endpoint on load
  const checkSession = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      fetchAntiforgeryToken().catch(() => {});
      const activeUser = await getCurrentUser();
      setUser(activeUser);
      if (activeUser) {
        setPendingTwoFactor(false);
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to verify session status';
      setUser(null);
      setError(msg);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    let ignore = false;

    async function initialVerify() {
      try {
        fetchAntiforgeryToken().catch(() => {});
        const activeUser = await getCurrentUser();
        if (!ignore) {
          setUser(activeUser);
        }
      } catch {
        if (!ignore) {
          setUser(null);
        }
      } finally {
        if (!ignore) {
          setIsLoading(false);
        }
      }
    }

    initialVerify();

    return () => {
      ignore = true;
    };
  }, []);

  const login = useCallback(async (credentials: LoginCredentials): Promise<{ success: boolean; requiresTwoFactor?: boolean }> => {
    setIsLoading(true);
    setError(null);
    try {
      const result = await loginUser(credentials);
      if (result.requiresTwoFactor) {
        setPendingTwoFactor(true);
        setUser(null);
        return { success: false, requiresTwoFactor: true };
      }

      setPendingTwoFactor(false);
      setUser(result.user ?? null);
      return { success: true, requiresTwoFactor: false };
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Sign-in failed. Please verify credentials.';
      setError(msg);
      return { success: false };
    } finally {
      setIsLoading(false);
    }
  }, []);

  const verifyChallenge = useCallback(async (code: string, isRecoveryCode?: boolean, rememberMe?: boolean): Promise<boolean> => {
    setIsLoading(true);
    setError(null);
    try {
      const authenticatedUser = await verifyTwoFactorChallenge({ code, isRecoveryCode, rememberMe });
      setUser(authenticatedUser);
      setPendingTwoFactor(false);
      return true;
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Two-factor verification failed.';
      setError(msg);
      return false;
    } finally {
      setIsLoading(false);
    }
  }, []);

  const cancelTwoFactorChallenge = useCallback(() => {
    setPendingTwoFactor(false);
    setError(null);
  }, []);

  const logout = useCallback(async (): Promise<void> => {
    setIsLoading(true);
    setError(null);
    try {
      await logoutUser();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Error during sign-out';
      setError(msg);
    } finally {
      setUser(null);
      setPendingTwoFactor(false);
      setIsLoading(false);
    }
  }, []);

  const clearSession = useCallback(() => {
    setUser(null);
    setPendingTwoFactor(false);
    setError(null);
  }, []);

  const clearError = useCallback(() => {
    setError(null);
  }, []);

  const value: AuthContextValue = {
    user,
    isAuthenticated: !!user,
    isLoading,
    error,
    pendingTwoFactor,
    login,
    verifyChallenge,
    cancelTwoFactorChallenge,
    logout,
    checkSession,
    clearSession,
    clearError,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
};
