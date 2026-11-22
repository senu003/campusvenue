import type {
  VenueDto,
  UpdateVenueRulesPayload,
  CheckAvailabilityRequest,
  CheckAvailabilityResponse,
  VenueCalendarCoverageDto,
  CreateCoverageRequest,
  VenueCalendarBlockDto,
  CreateBlockRequest,
  BookingRequestDto,
  CreateBookingRequestPayload,
  ReviewBookingRequestPayload,
  AssignVenueManagerPayload,
  AssignVenueManagerResponse,
} from './types';
import { fetchAntiforgeryToken, getCachedAntiforgeryToken } from '../auth/api';

export class VenueForbiddenError extends Error {
  statusCode: number;

  constructor(message = 'Access denied: You are not authorized to perform this venue action.') {
    super(message);
    this.name = 'VenueForbiddenError';
    this.statusCode = 403;
  }
}

export class VenueAuthError extends Error {
  statusCode: number;

  constructor(message = 'Authentication required. Please sign in.') {
    super(message);
    this.name = 'VenueAuthError';
    this.statusCode = 401;
  }
}

export async function fetchVenues(): Promise<VenueDto[]> {
  const response = await fetch('/api/venues', {
    method: 'GET',
    headers: {
      'Accept': 'application/json',
    },
    credentials: 'include',
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (response.status === 403) {
    throw new VenueForbiddenError();
  }

  if (!response.ok) {
    throw new Error(`Failed to load venues (HTTP ${response.status})`);
  }

  return response.json();
}

export async function checkVenueAvailability(
  venueId: string,
  request: CheckAvailabilityRequest
): Promise<CheckAvailabilityResponse> {
  const response = await fetch(`/api/venues/${venueId}/check-availability`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    },
    credentials: 'include',
    body: JSON.stringify(request),
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (!response.ok) {
    const errorData = await response.json().catch(() => null);
    const message = (errorData && typeof errorData === 'object' && 'message' in errorData && typeof errorData.message === 'string')
      ? errorData.message
      : `Availability check failed (HTTP ${response.status})`;
    throw new Error(message);
  }

  return response.json();
}

export async function updateVenueRules(
  venueId: string,
  payload: UpdateVenueRulesPayload
): Promise<VenueDto> {
  let token = getCachedAntiforgeryToken();
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch(`/api/venues/${venueId}/rules`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify(payload),
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (response.status === 403) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'You are not authorized to update rules for this venue.';
    throw new VenueForbiddenError(detail);
  }

  if (!response.ok) {
    const errorData = await response.json().catch(() => null);
    const message = (errorData && typeof errorData === 'object' && 'message' in errorData && typeof errorData.message === 'string')
      ? errorData.message
      : `Failed to update venue rules (HTTP ${response.status})`;
    throw new Error(message);
  }

  return response.json();
}

export async function fetchCalendarCoverages(venueId: string): Promise<VenueCalendarCoverageDto[]> {
  const response = await fetch(`/api/venues/${venueId}/calendar-coverages`, {
    method: 'GET',
    headers: {
      'Accept': 'application/json',
    },
    credentials: 'include',
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (!response.ok) {
    throw new Error(`Failed to load calendar coverages (HTTP ${response.status})`);
  }

  return response.json();
}

export async function addCalendarCoverage(
  venueId: string,
  request: CreateCoverageRequest
): Promise<VenueCalendarCoverageDto> {
  let token = getCachedAntiforgeryToken();
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch(`/api/venues/${venueId}/calendar-coverages`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify(request),
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (response.status === 403) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'You are not authorized to add calendar coverage for this venue.';
    throw new VenueForbiddenError(detail);
  }

  if (!response.ok) {
    const errorData = await response.json().catch(() => null);
    const message = (errorData && typeof errorData === 'object' && 'message' in errorData && typeof errorData.message === 'string')
      ? errorData.message
      : `Failed to add calendar coverage (HTTP ${response.status})`;
    throw new Error(message);
  }

  return response.json();
}

export async function fetchCalendarBlocks(
  venueId: string,
  fromUtc?: string,
  toUtc?: string
): Promise<VenueCalendarBlockDto[]> {
  const queryParams = new URLSearchParams();
  if (fromUtc) queryParams.set('fromUtc', fromUtc);
  if (toUtc) queryParams.set('toUtc', toUtc);

  const query = queryParams.toString();
  const url = query ? `/api/venues/${venueId}/calendar-blocks?${query}` : `/api/venues/${venueId}/calendar-blocks`;

  const response = await fetch(url, {
    method: 'GET',
    headers: {
      'Accept': 'application/json',
    },
    credentials: 'include',
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (!response.ok) {
    throw new Error(`Failed to load calendar blocks (HTTP ${response.status})`);
  }

  return response.json();
}

export async function addCalendarBlock(
  venueId: string,
  request: CreateBlockRequest
): Promise<VenueCalendarBlockDto> {
  let token = getCachedAntiforgeryToken();
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch(`/api/venues/${venueId}/calendar-blocks`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify(request),
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (response.status === 403) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'You are not authorized to add calendar blocks for this venue.';
    throw new VenueForbiddenError(detail);
  }

  if (!response.ok) {
    const errorData = await response.json().catch(() => null);
    const message = (errorData && typeof errorData === 'object' && 'message' in errorData && typeof errorData.message === 'string')
      ? errorData.message
      : `Failed to add calendar block (HTTP ${response.status})`;
    throw new Error(message);
  }

  return response.json();
}

export async function deleteCalendarBlock(venueId: string, blockId: string): Promise<void> {
  let token = getCachedAntiforgeryToken();
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch(`/api/venues/${venueId}/calendar-blocks/${blockId}`, {
    method: 'DELETE',
    headers: {
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (response.status === 403) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'You are not authorized to delete calendar blocks for this venue.';
    throw new VenueForbiddenError(detail);
  }

  if (!response.ok) {
    throw new Error(`Failed to delete calendar block (HTTP ${response.status})`);
  }
}

// ============================================================================
// Phase 1C.2: Booking Requests and Venue Review API Client
// ============================================================================

export async function submitBookingRequest(
  venueId: string,
  payload: CreateBookingRequestPayload
): Promise<BookingRequestDto> {
  let token = getCachedAntiforgeryToken();
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch(`/api/venues/${venueId}/booking-requests`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify(payload),
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (response.status === 403) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'You are not authorized to submit booking requests. Event Coordinator role required.';
    throw new VenueForbiddenError(detail);
  }

  if (!response.ok) {
    const errorData = await response.json().catch(() => null);
    const message = (errorData && typeof errorData === 'object' && 'message' in errorData && typeof errorData.message === 'string')
      ? errorData.message
      : `Failed to submit booking request (HTTP ${response.status})`;
    throw new Error(message);
  }

  return response.json();
}

export async function fetchMyBookingRequests(): Promise<BookingRequestDto[]> {
  const response = await fetch('/api/booking-requests/my', {
    method: 'GET',
    headers: {
      'Accept': 'application/json',
    },
    credentials: 'include',
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (!response.ok) {
    throw new Error(`Failed to load booking requests (HTTP ${response.status})`);
  }

  return response.json();
}

export async function fetchReviewQueue(): Promise<BookingRequestDto[]> {
  const response = await fetch('/api/booking-requests/review-queue', {
    method: 'GET',
    headers: {
      'Accept': 'application/json',
    },
    credentials: 'include',
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (response.status === 403) {
    throw new VenueForbiddenError('You are not authorized to view the review queue.');
  }

  if (!response.ok) {
    throw new Error(`Failed to load review queue (HTTP ${response.status})`);
  }

  return response.json();
}

export async function fetchVenueBookingRequests(venueId: string): Promise<BookingRequestDto[]> {
  const response = await fetch(`/api/venues/${venueId}/booking-requests`, {
    method: 'GET',
    headers: {
      'Accept': 'application/json',
    },
    credentials: 'include',
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (response.status === 403) {
    throw new VenueForbiddenError('You are not authorized to view requests for this venue.');
  }

  if (!response.ok) {
    throw new Error(`Failed to load venue booking requests (HTTP ${response.status})`);
  }

  return response.json();
}

export async function reviewBookingRequest(
  requestId: string,
  payload: ReviewBookingRequestPayload
): Promise<BookingRequestDto> {
  let token = getCachedAntiforgeryToken();
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch(`/api/booking-requests/${requestId}/review`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify(payload),
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (response.status === 403) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'You are not authorized to review this booking request.';
    throw new VenueForbiddenError(detail);
  }

  if (!response.ok) {
    const errorData = await response.json().catch(() => null);
    const message = (errorData && typeof errorData === 'object' && 'message' in errorData && typeof errorData.message === 'string')
      ? errorData.message
      : `Failed to review booking request (HTTP ${response.status})`;
    throw new Error(message);
  }

  return response.json();
}

export async function respondToConditions(
  requestId: string,
  accept: boolean
): Promise<BookingRequestDto> {
  let token = getCachedAntiforgeryToken();
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch(`/api/booking-requests/${requestId}/respond-conditions`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify({ accept }),
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (response.status === 403) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'You are not authorized to respond to conditions for this request.';
    throw new VenueForbiddenError(detail);
  }

  if (!response.ok) {
    const errorData = await response.json().catch(() => null);
    const message = (errorData && typeof errorData === 'object' && 'message' in errorData && typeof errorData.message === 'string')
      ? errorData.message
      : `Failed to respond to conditions (HTTP ${response.status})`;
    throw new Error(message);
  }

  return response.json();
}

export async function assignVenueManager(
  venueId: string,
  payload: AssignVenueManagerPayload
): Promise<AssignVenueManagerResponse> {
  let token = getCachedAntiforgeryToken();
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch(`/api/venues/${venueId}/managers`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
    body: JSON.stringify(payload),
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (response.status === 403) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'You are not authorized to assign venue managers. University Administrator privileges and a completed MFA session are required.';
    throw new VenueForbiddenError(detail);
  }

  if (!response.ok) {
    const errorData = await response.json().catch(() => null);
    const message = (errorData && typeof errorData === 'object' && 'message' in errorData && typeof errorData.message === 'string')
      ? errorData.message
      : `Failed to assign venue manager (HTTP ${response.status})`;
    throw new Error(message);
  }

  return response.json();
}

export async function confirmBookingRequest(requestId: string): Promise<BookingRequestDto> {
  let token = getCachedAntiforgeryToken();
  if (!token) {
    token = await fetchAntiforgeryToken();
  }

  const response = await fetch(`/api/booking-requests/${requestId}/confirm`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    credentials: 'include',
  });

  if (response.status === 401) {
    throw new VenueAuthError();
  }

  if (response.status === 403) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'You are not authorized to confirm this booking request.';
    throw new VenueForbiddenError(detail);
  }

  if (response.status === 409) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : 'The venue is no longer available for the requested time. The request remains awaiting confirmation.';
    throw new Error(detail);
  }

  if (!response.ok) {
    const errorData = await response.json().catch(() => null);
    const detail = (errorData && typeof errorData === 'object' && 'detail' in errorData && typeof errorData.detail === 'string')
      ? errorData.detail
      : (errorData && typeof errorData === 'object' && 'message' in errorData && typeof errorData.message === 'string')
      ? errorData.message
      : `Failed to confirm booking request (HTTP ${response.status})`;
    throw new Error(detail);
  }

  return response.json();
}
