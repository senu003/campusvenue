import React, { useState } from 'react';
import { CalendarCheck, CheckCircle2, RefreshCw } from 'lucide-react';
import { confirmBookingRequest } from './api';
import type { BookingRequestDto } from './types';

export interface ConfirmBookingActionProps {
  bookingRequest: BookingRequestDto;
  onConfirmed: (confirmedRequest: BookingRequestDto) => void;
  onError?: (errorMessage: string) => void;
}

export const ConfirmBookingAction: React.FC<ConfirmBookingActionProps> = ({
  bookingRequest,
  onConfirmed,
  onError,
}) => {
  const [isConfirming, setIsConfirming] = useState(false);

  const handleConfirm = async () => {
    setIsConfirming(true);
    try {
      const confirmed = await confirmBookingRequest(bookingRequest.id);
      onConfirmed(confirmed);
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to confirm booking reservation.';
      onError?.(message);
    } finally {
      setIsConfirming(false);
    }
  };

  return (
    <div className="p-4 bg-blue-50/70 border border-blue-200 rounded-lg space-y-3">
      <div className="flex items-center justify-between gap-2 flex-wrap">
        <div className="flex items-center gap-2 text-blue-900 font-bold">
          <CheckCircle2 className="h-4 w-4 text-blue-600 shrink-0" />
          <span>Request Approved & Awaiting Final Booking Confirmation</span>
        </div>
        <button
          type="button"
          disabled={isConfirming}
          onClick={handleConfirm}
          className="px-4 py-1.5 bg-emerald-600 hover:bg-emerald-700 text-white rounded font-semibold flex items-center gap-1.5 shadow-sm disabled:opacity-50"
        >
          {isConfirming ? (
            <>
              <RefreshCw className="h-3.5 w-3.5 animate-spin" />
              <span>Confirming...</span>
            </>
          ) : (
            <>
              <CalendarCheck className="h-3.5 w-3.5" />
              <span>Confirm Booking</span>
            </>
          )}
        </button>
      </div>
      <p className="text-[11px] text-blue-800">
        The venue custodian has approved this request. Click <strong>Confirm Booking</strong> to perform final double-booking verification and generate your official reservation reference.
      </p>
      {bookingRequest.reviewNotes && (
        <p className="text-[11px] text-gray-600 italic">
          Reviewer Notes: {bookingRequest.reviewNotes}
        </p>
      )}
    </div>
  );
};
