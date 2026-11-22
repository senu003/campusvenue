export interface VenueManagerDto {
  userId: string;
  email: string;
  displayName: string | null;
  assignedAtUtc: string;
}

export interface VenueDto {
  id: string;
  name: string;
  code: string;
  type: string;
  managingUnit: string;
  capacity: number;
  operatingHoursStart: string;
  operatingHoursEnd: string;
  minNoticeHours: number;
  defaultSetupMinutes: number;
  defaultCleanupMinutes: number;
  maxBookingHours: number;
  requiresEventApproval: boolean;
  generalRules: string | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  managers: VenueManagerDto[];
}

export interface UpdateVenueRulesPayload {
  operatingHoursStart: string;
  operatingHoursEnd: string;
  capacity: number;
  minNoticeHours: number;
  defaultSetupMinutes: number;
  defaultCleanupMinutes: number;
  maxBookingHours: number;
  requiresEventApproval: boolean;
  generalRules: string | null;
}

export interface ConflictDetailsDto {
  blockId: string;
  title: string;
  blockType: string;
  startUtc: string;
  endUtc: string;
  isBufferConflictOnly: boolean;
  conflictScope: 'Event' | 'SetupBuffer' | 'CleanupBuffer' | string;
}

export interface CoverageDetailsDto {
  sourceName: string;
  sourceReference: string | null;
  coversFromUtc: string;
  coversToUtc: string;
  verifiedBy: string;
  verifiedAtUtc: string;
}

export type AvailabilityStatusType = 'Available' | 'Unavailable' | 'Availability unverified';

export interface CheckAvailabilityResponse {
  venueId: string;
  venueName: string;
  venueCode: string;
  status: AvailabilityStatusType;
  reason: string;
  isAvailable: boolean;
  eventStartUtc: string;
  eventEndUtc: string;
  occupiedStartUtc: string;
  occupiedEndUtc: string;
  setupMinutes: number;
  cleanupMinutes: number;
  conflictDetails: ConflictDetailsDto | null;
  coverageDetails: CoverageDetailsDto | null;
}

export interface CheckAvailabilityRequest {
  eventStartUtc: string;
  eventEndUtc: string;
  setupMinutes?: number;
  cleanupMinutes?: number;
}

export interface VenueCalendarCoverageDto {
  id: string;
  venueId: string;
  sourceName: string;
  sourceReference: string | null;
  coversFromUtc: string;
  coversToUtc: string;
  verifiedByUserId: string;
  verifiedByUserEmail: string;
  verifiedAtUtc: string;
  notes: string | null;
  createdAtUtc: string;
}

export interface CreateCoverageRequest {
  sourceName: string;
  sourceReference?: string;
  coversFromUtc: string;
  coversToUtc: string;
  notes?: string;
}

export interface VenueCalendarBlockDto {
  id: string;
  venueId: string;
  title: string;
  blockType: string;
  startUtc: string;
  endUtc: string;
  createdByUserId: string;
  createdByUserEmail: string;
  createdAtUtc: string;
}

export interface CreateBlockRequest {
  title: string;
  blockType: string;
  startUtc: string;
  endUtc: string;
}

export type BookingRequestStatusType =
  | 'PendingReview'
  | 'ApprovedWithConditions'
  | 'AwaitingConfirmation'
  | 'Rejected'
  | 'Cancelled';

export interface BookingNotificationDto {
  id: string;
  bookingRequestId: string;
  recipientUserId: string;
  recipientEmail: string;
  notificationType: string;
  subject: string;
  sentAtUtc: string;
  createdAtUtc: string;
}

export interface BookingRequestDto {
  id: string;
  venueId: string;
  venueName: string;
  venueCode: string;
  requesterId: string;
  requesterEmail: string;
  requesterName: string | null;
  eventTitle: string;
  description: string | null;
  expectedAttendance: number;
  eventStartUtc: string;
  eventEndUtc: string;
  setupMinutes: number;
  cleanupMinutes: number;
  status: BookingRequestStatusType;
  reviewNotes: string | null;
  conditions: string | null;
  conditionsAccepted: boolean | null;
  reviewedByUserId: string | null;
  reviewedByEmail: string | null;
  reviewedAtUtc: string | null;
  idempotencyKey: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  notifications: BookingNotificationDto[];
}

export interface CreateBookingRequestPayload {
  eventTitle: string;
  description?: string;
  expectedAttendance: number;
  eventStartUtc: string;
  eventEndUtc: string;
  setupMinutes?: number;
  cleanupMinutes?: number;
  idempotencyKey?: string;
}

export interface ReviewBookingRequestPayload {
  decision: 'Approve' | 'ApproveWithConditions' | 'Reject';
  notes?: string;
  conditions?: string;
}

export interface RespondConditionsPayload {
  accept: boolean;
}

export interface AssignVenueManagerPayload {
  userId: string;
}

export interface AssignVenueManagerResponse {
  message: string;
}
