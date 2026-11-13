import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import App from './App';

describe('App Component', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('renders application brand and header correctly', async () => {
    // Mock successful health fetch
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({
        status: 'Healthy',
        totalDurationMs: 4.2,
        timestamp: new Date().toISOString(),
        environment: 'Development',
        checks: [
          {
            name: 'postgresql',
            status: 'Healthy',
            description: 'Reachable and responsive',
            durationMs: 3.1,
          },
        ],
      }),
    } as Response);

    render(<App />);

    expect(screen.getByText('CampusVenue')).toBeInTheDocument();
    expect(screen.getByText('Phase 0 Foundation')).toBeInTheDocument();

    await waitFor(() => {
      const healthyBadges = screen.getAllByText('Healthy');
      expect(healthyBadges.length).toBeGreaterThanOrEqual(1);
      expect(screen.getByText('Development')).toBeInTheDocument();
    });
  });

  it('renders error state when backend health check fails', async () => {
    // Mock network failure
    vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new Error('Network connection refused'));

    render(<App />);

    await waitFor(() => {
      expect(screen.getByRole('alert')).toBeInTheDocument();
      expect(screen.getByText(/Unable to establish backend connectivity/i)).toBeInTheDocument();
      expect(screen.getByText(/Network connection refused/i)).toBeInTheDocument();
    });
  });
});
