import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ConfirmBookingAction } from './ConfirmBookingAction';
import * as api from './api';
import type { BookingRequestDto } from './types';

describe('ConfirmBookingAction Component Tests', () => {
  const mockBookingRequest: BookingRequestDto = {
    id: 'req-test-123',
    venueId: 'venue-1',
    venueName: 'Civil Auditorium (J.G. Hall)',
    venueCode: 'JG-HALL',
    requesterId: 'user-coord-1',
    requesterEmail: 'coordinator@campus.internal',
    requesterName: 'Coordinator',
    eventTitle: 'Annual Robotics Symposium',
    description: 'Robotics demos and presentations',
    expectedAttendance: 120,
    eventStartUtc: '2026-11-28T04:30:00Z',
    eventEndUtc: '2026-11-28T08:30:00Z',
    setupMinutes: 30,
    cleanupMinutes: 30,
    status: 'AwaitingConfirmation',
    reviewNotes: 'Approved for stage access and equipment setup.',
    conditions: null,
    conditionsAccepted: null,
    reviewedByUserId: 'mgr-1',
    reviewedByEmail: 'manager@campus.internal',
    reviewedAtUtc: '2026-11-22T02:00:00Z',
    idempotencyKey: null,
    createdAtUtc: '2026-11-22T00:00:00Z',
    updatedAtUtc: '2026-11-22T02:00:00Z',
    notifications: [],
  };

  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('renders awaiting confirmation prompt, reviewer notes, and confirm action button', () => {
    render(
      <ConfirmBookingAction
        bookingRequest={mockBookingRequest}
        onConfirmed={vi.fn()}
        onError={vi.fn()}
      />
    );

    expect(screen.getByText(/Request Approved & Awaiting Final Booking Confirmation/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Confirm Booking/i })).toBeInTheDocument();
    expect(screen.getByText(/Approved for stage access and equipment setup\./i)).toBeInTheDocument();
    expect(screen.getByText(/The venue custodian has approved this request/i)).toBeInTheDocument();
  });

  it('calls confirmBookingRequest and triggers onConfirmed callback with confirmed reservation payload', async () => {
    const onConfirmed = vi.fn();
    const onError = vi.fn();

    const mockConfirmed: BookingRequestDto = {
      ...mockBookingRequest,
      status: 'Confirmed',
      reservationReference: 'RES-20261122-XYZ999',
      confirmedReservationId: 'res-uuid-123',
      confirmedAtUtc: '2026-11-22T03:00:00Z',
    };

    const confirmSpy = vi.spyOn(api, 'confirmBookingRequest').mockResolvedValue(mockConfirmed);

    render(
      <ConfirmBookingAction
        bookingRequest={mockBookingRequest}
        onConfirmed={onConfirmed}
        onError={onError}
      />
    );

    const button = screen.getByRole('button', { name: /Confirm Booking/i });
    fireEvent.click(button);

    await waitFor(() => {
      expect(confirmSpy).toHaveBeenCalledWith('req-test-123');
      expect(onConfirmed).toHaveBeenCalledWith(mockConfirmed);
      expect(onError).not.toHaveBeenCalled();
    });
  });

  it('handles conflict or API error gracefully and triggers onError callback', async () => {
    const onConfirmed = vi.fn();
    const onError = vi.fn();

    vi.spyOn(api, 'confirmBookingRequest').mockRejectedValue(
      new Error('Conflict detected: Venue is occupied by a concurrent reservation.')
    );

    render(
      <ConfirmBookingAction
        bookingRequest={mockBookingRequest}
        onConfirmed={onConfirmed}
        onError={onError}
      />
    );

    const button = screen.getByRole('button', { name: /Confirm Booking/i });
    fireEvent.click(button);

    await waitFor(() => {
      expect(onError).toHaveBeenCalledWith('Conflict detected: Venue is occupied by a concurrent reservation.');
      expect(onConfirmed).not.toHaveBeenCalled();
    });
  });
});
