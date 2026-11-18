import React, { useState, useEffect, useCallback, type ReactNode } from 'react';
import type { User, LoginCredentials } from './types';
import { getCurrentUser, loginUser, logoutUser, fetchAntiforgeryToken } from './api';
import { AuthContext, type AuthContextValue } from './context';

export const AuthProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  // Restore session from authoritative /api/auth/me endpoint on load
  const checkSession = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      fetchAntiforgeryToken().catch(() => {});
      const activeUser = await getCurrentUser();
      setUser(activeUser);
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

  const login = useCallback(async (credentials: LoginCredentials): Promise<boolean> => {
    setIsLoading(true);
    setError(null);
    try {
      const authenticatedUser = await loginUser(credentials);
      setUser(authenticatedUser);
      return true;
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Sign-in failed. Please verify credentials.';
      setError(msg);
      return false;
    } finally {
      setIsLoading(false);
    }
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
      setIsLoading(false);
    }
  }, []);

  const clearSession = useCallback(() => {
    setUser(null);
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
    login,
    logout,
    checkSession,
    clearSession,
    clearError,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
};
