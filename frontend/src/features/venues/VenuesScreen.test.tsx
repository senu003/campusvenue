import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { VenuesScreen } from './VenuesScreen';
import * as api from './api';
import * as usersApi from '../users/api';
import type { VenueDto, CheckAvailabilityResponse, BookingRequestDto, BuildingDto } from './types';
import { useAuth } from '../auth/useAuth';
import type { AuthContextValue } from '../auth/context';

const createMockAuth = (overrides?: Partial<AuthContextValue>): AuthContextValue => ({
  user: {
    id: 'user-coord-1',
    email: 'coordinator@campus.internal',
    roles: ['EventCoordinator'],
    mfaCompleted: false,
    ...overrides?.user,
  },
  isAuthenticated: overrides?.isAuthenticated ?? true,
  isLoading: overrides?.isLoading ?? false,
  error: overrides?.error ?? null,
  pendingTwoFactor: overrides?.pendingTwoFactor ?? false,
  login: vi.fn(),
  logout: vi.fn(),
  verifyChallenge: vi.fn(),
  cancelTwoFactorChallenge: vi.fn(),
  checkSession: vi.fn(),
  clearSession: vi.fn(),
  clearError: vi.fn(),
  ...overrides,
});

// Mock Auth hook
vi.mock('../auth/useAuth', () => ({
  useAuth: vi.fn(() => createMockAuth()),
}));

describe('VenuesScreen Component Tests', () => {
  const mockBuildings: BuildingDto[] = [
    {
      id: 'bld-civil',
      name: 'Civil Engineering Complex',
      code: 'BLD-CIVIL',
      venueCount: 1,
      createdAtUtc: '2026-11-20T00:00:00Z',
      updatedAtUtc: '2026-11-20T00:00:00Z',
    },
    {
      id: 'bld-fob',
      name: 'Faculty of Business Tower',
      code: 'BLD-FOB',
      venueCount: 0,
      createdAtUtc: '2026-11-20T00:00:00Z',
      updatedAtUtc: '2026-11-20T00:00:00Z',
    },
  ];

  const mockVenues: VenueDto[] = [
    {
      id: 'venue-jg-hall',
      name: 'Civil Auditorium (J.G. Hall)',
      code: 'JG-HALL',
      buildingId: 'bld-civil',
      buildingName: 'Civil Engineering Complex',
      buildingCode: 'BLD-CIVIL',
      type: 'Central',
      managingUnit: 'General Administration / Student Welfare',
      capacity: 600,
      operatingHoursStart: '08:00',
      operatingHoursEnd: '20:00',
      minNoticeHours: 72,
      defaultSetupMinutes: 45,
      defaultCleanupMinutes: 30,
      maxBookingHours: 8,
      requiresEventApproval: true,
      generalRules: 'No food or open drinks inside the hall. Audio-visual equipment requires briefing.',
      isActive: true,
      createdAtUtc: '2026-11-19T00:00:00Z',
      updatedAtUtc: '2026-11-19T00:00:00Z',
      managers: [],
    },
    {
      id: 'venue-fob-101',
      name: 'Faculty of Business Room 101',
      code: 'FOB-101',
      buildingId: null,
      buildingName: null,
      buildingCode: null,
      type: 'Departmental',
      managingUnit: 'Faculty of Business / Undergraduate Studies',
      capacity: 80,
      operatingHoursStart: '08:00',
      operatingHoursEnd: '18:00',
      minNoticeHours: 24,
      defaultSetupMinutes: 15,
      defaultCleanupMinutes: 15,
      maxBookingHours: 4,
      requiresEventApproval: false,
      generalRules: 'Department lecture room guidelines apply. Return whiteboard markers.',
      isActive: true,
      createdAtUtc: '2026-11-19T00:00:00Z',
      updatedAtUtc: '2026-11-19T00:00:00Z',
      managers: [],
    },
  ];

  beforeEach(() => {
    vi.restoreAllMocks();
    vi.spyOn(api, 'fetchVenues').mockResolvedValue(mockVenues);
    vi.spyOn(api, 'fetchBuildings').mockResolvedValue(mockBuildings);
    vi.spyOn(api, 'fetchCalendarCoverages').mockResolvedValue([]);
    vi.spyOn(api, 'fetchCalendarBlocks').mockResolvedValue([]);
    vi.spyOn(api, 'fetchMyBookingRequests').mockResolvedValue([]);
    vi.spyOn(api, 'fetchReviewQueue').mockResolvedValue([]);
    vi.spyOn(usersApi, 'fetchUserDirectory').mockResolvedValue({
      items: [
        {
          id: 'facility-user-1',
          email: 'facility@campus.internal',
          roles: ['FacilityAdmin'],
          emailConfirmed: true,
          twoFactorEnabled: false,
          createdAtUtc: '2026-11-20T00:00:00Z',
        },
        {
          id: 'coord-user-1',
          email: 'coordinator@campus.internal',
          roles: ['EventCoordinator'],
          emailConfirmed: true,
          twoFactorEnabled: false,
          createdAtUtc: '2026-11-20T00:00:00Z',
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 2,
      totalPages: 1,
    });
  });

  it('renders venue catalog with central hall and department room', async () => {
    render(<VenuesScreen />);

    expect(screen.getByText(/Venues & Booking Requests/i)).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText('Civil Auditorium (J.G. Hall)')).toBeInTheDocument();
      expect(screen.getByText('Faculty of Business Room 101')).toBeInTheDocument();
    });

    expect(screen.getByText('Central Managed')).toBeInTheDocument();
    expect(screen.getByText('Departmental Managed')).toBeInTheDocument();
    expect(screen.getByText(/600/)).toBeInTheDocument();
    expect(screen.getByText(/80/)).toBeInTheDocument();
  });

  it('renders general venue rules on venue catalog cards', async () => {
    render(<VenuesScreen />);

    await waitFor(() => {
      expect(screen.getByText(/No food or open drinks inside the hall/i)).toBeInTheDocument();
    });
  });

  it('checks availability and renders "Available" result with coverage details', async () => {
    const mockAvailableResponse: CheckAvailabilityResponse = {
      venueId: 'venue-jg-hall',
      venueName: 'Civil Auditorium (J.G. Hall)',
      venueCode: 'JG-HALL',
      status: 'Available',
      reason: 'Venue is available for the requested event window and required setup/cleanup buffers.',
      isAvailable: true,
      eventStartUtc: '2026-11-25T04:30:00Z',
      eventEndUtc: '2026-11-25T06:30:00Z',
      occupiedStartUtc: '2026-11-25T03:45:00Z',
      occupiedEndUtc: '2026-11-25T07:00:00Z',
      setupMinutes: 45,
      cleanupMinutes: 30,
      conflictDetails: null,
      coverageDetails: {
        sourceName: 'CITeS RBS Staff System (book.uom.lk)',
        sourceReference: 'Semester 2 Master Calendar',
        coversFromUtc: '2026-11-01T00:00:00Z',
        coversToUtc: '2026-12-31T00:00:00Z',
        verifiedBy: 'admin@campus.internal',
        verifiedAtUtc: '2026-11-19T10:00:00Z',
      },
    };

    vi.spyOn(api, 'checkVenueAvailability').mockResolvedValue(mockAvailableResponse);

    render(<VenuesScreen />);

    await waitFor(() => {
      expect(screen.getByText('Civil Auditorium (J.G. Hall)')).toBeInTheDocument();
    });

    const checkBtn = screen.getByRole('button', { name: /^Check Availability$/i });
    fireEvent.click(checkBtn);

    await waitFor(() => {
      expect(screen.getByText('Available')).toBeInTheDocument();
      expect(screen.getByText(/Venue is available for the requested event window/i)).toBeInTheDocument();
      expect(screen.getByText(/CITeS RBS Staff System/i)).toBeInTheDocument();
    });
  });

  it('shows "No venue manager configured" warning and disables request when managers list is empty', async () => {
    const mockAvailableResponse: CheckAvailabilityResponse = {
      venueId: 'venue-jg-hall',
      venueName: 'Civil Auditorium (J.G. Hall)',
      venueCode: 'JG-HALL',
      status: 'Available',
      reason: 'Venue is available.',
      isAvailable: true,
      eventStartUtc: '2026-11-25T04:30:00Z',
      eventEndUtc: '2026-11-25T06:30:00Z',
      occupiedStartUtc: '2026-11-25T03:45:00Z',
      occupiedEndUtc: '2026-11-25T07:00:00Z',
      setupMinutes: 45,
      cleanupMinutes: 30,
      conflictDetails: null,
      coverageDetails: null,
    };

    vi.spyOn(api, 'checkVenueAvailability').mockResolvedValue(mockAvailableResponse);

    render(<VenuesScreen />);

    await waitFor(() => {
      expect(screen.getByText('Civil Auditorium (J.G. Hall)')).toBeInTheDocument();
    });

    const checkBtn = screen.getByRole('button', { name: /^Check Availability$/i });
    fireEvent.click(checkBtn);

    await waitFor(() => {
      expect(screen.getByText(/No venue manager configured/i)).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /Request This Venue/i })).not.toBeInTheDocument();
    });
  });

  it('allows submitting a booking request when venue has assigned managers and rules are acknowledged', async () => {
    const venuesWithManager: VenueDto[] = [
      {
        ...mockVenues[0],
        managers: [
          {
            userId: 'mgr-1',
            email: 'manager@campus.internal',
            displayName: 'Venue Manager',
            assignedAtUtc: '2026-11-20T00:00:00Z',
          },
        ],
      },
    ];
    vi.spyOn(api, 'fetchVenues').mockResolvedValue(venuesWithManager);

    const mockAvailableResponse: CheckAvailabilityResponse = {
      venueId: 'venue-jg-hall',
      venueName: 'Civil Auditorium (J.G. Hall)',
      venueCode: 'JG-HALL',
      status: 'Available',
      reason: 'Venue is available.',
      isAvailable: true,
      eventStartUtc: '2026-11-25T04:30:00Z',
      eventEndUtc: '2026-11-25T06:30:00Z',
      occupiedStartUtc: '2026-11-25T03:45:00Z',
      occupiedEndUtc: '2026-11-25T07:00:00Z',
      setupMinutes: 45,
      cleanupMinutes: 30,
      conflictDetails: null,
      coverageDetails: null,
    };
    vi.spyOn(api, 'checkVenueAvailability').mockResolvedValue(mockAvailableResponse);

    const submitSpy = vi.spyOn(api, 'submitBookingRequest').mockResolvedValue({
      id: 'req-1',
      venueId: 'venue-jg-hall',
      venueName: 'Civil Auditorium (J.G. Hall)',
      venueCode: 'JG-HALL',
      requesterId: 'user-coord-1',
      requesterEmail: 'coordinator@campus.internal',
      requesterName: 'Coordinator',
      eventTitle: 'Hackathon 2026',
      description: 'Annual coding event',
      expectedAttendance: 100,
      eventStartUtc: '2026-11-25T04:30:00Z',
      eventEndUtc: '2026-11-25T06:30:00Z',
      setupMinutes: 45,
      cleanupMinutes: 30,
      status: 'PendingReview',
      reviewNotes: null,
      conditions: null,
      conditionsAccepted: null,
      reviewedByUserId: null,
      reviewedByEmail: null,
      reviewedAtUtc: null,
      idempotencyKey: 'idemp-1',
      createdAtUtc: '2026-11-22T00:00:00Z',
      updatedAtUtc: '2026-11-22T00:00:00Z',
      notifications: [],
    });

    render(<VenuesScreen />);

    await waitFor(() => {
      expect(screen.getByText('Civil Auditorium (J.G. Hall)')).toBeInTheDocument();
    });

    // Check availability
    fireEvent.click(screen.getByRole('button', { name: /^Check Availability$/i }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Request This Venue/i })).toBeInTheDocument();
    });

    // Open modal
    fireEvent.click(screen.getByRole('button', { name: /Request This Venue/i }));

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /Submit Booking Request/i })).toBeInTheDocument();
    });

    // Fill form
    fireEvent.change(screen.getByPlaceholderText(/Annual Student Robotics/i), {
      target: { value: 'Hackathon 2026' },
    });

    // Check the rules acknowledgment checkbox
    const checkbox = screen.getByRole('checkbox');
    fireEvent.click(checkbox);

    // Submit request
    fireEvent.click(screen.getByRole('button', { name: /^Submit Request$/i }));

    await waitFor(() => {
      expect(submitSpy).toHaveBeenCalledWith(
        'venue-jg-hall',
        expect.objectContaining({
          eventTitle: 'Hackathon 2026',
        })
      );
      expect(screen.getByText(/submitted successfully/i)).toBeInTheDocument();
    });
  });

  it('renders "My Requests" tab with conditions and lets coordinator accept conditions', async () => {
    const mockRequestWithConditions: BookingRequestDto = {
      id: 'req-cond-1',
      venueId: 'venue-jg-hall',
      venueName: 'Civil Auditorium (J.G. Hall)',
      venueCode: 'JG-HALL',
      requesterId: 'user-coord-1',
      requesterEmail: 'coordinator@campus.internal',
      requesterName: 'Coordinator',
      eventTitle: 'Music Festival 2026',
      description: null,
      expectedAttendance: 200,
      eventStartUtc: '2026-11-28T04:30:00Z',
      eventEndUtc: '2026-11-28T08:30:00Z',
      setupMinutes: 45,
      cleanupMinutes: 30,
      status: 'ApprovedWithConditions',
      reviewNotes: 'Sound level restrictions apply',
      conditions: '1. No amplified music after 19:00.\n2. Hire 2 campus guards.',
      conditionsAccepted: null,
      reviewedByUserId: 'mgr-1',
      reviewedByEmail: 'manager@campus.internal',
      reviewedAtUtc: '2026-11-22T02:00:00Z',
      idempotencyKey: null,
      createdAtUtc: '2026-11-22T00:00:00Z',
      updatedAtUtc: '2026-11-22T02:00:00Z',
      notifications: [],
    };

    vi.spyOn(api, 'fetchMyBookingRequests').mockResolvedValue([mockRequestWithConditions]);
    const respondSpy = vi.spyOn(api, 'respondToConditions').mockResolvedValue({
      ...mockRequestWithConditions,
      status: 'AwaitingConfirmation',
      conditionsAccepted: true,
    });

    render(<VenuesScreen />);

    // Click My Requests tab
    const myReqBtn = screen.getByRole('button', { name: /My Requests/i });
    fireEvent.click(myReqBtn);

    await waitFor(() => {
      expect(screen.getByText('Music Festival 2026')).toBeInTheDocument();
      expect(screen.getByText(/Conditions Pending Acceptance/i)).toBeInTheDocument();
      expect(screen.getByText(/No amplified music after 19:00/i)).toBeInTheDocument();
    });

    // Accept conditions
    const acceptBtn = screen.getByRole('button', { name: /Accept Conditions/i });
    fireEvent.click(acceptBtn);

    await waitFor(() => {
      expect(respondSpy).toHaveBeenCalledWith('req-cond-1', true);
    });
  });

  it('renders "Review Queue" with segregation of duties badge when requester is current user', async () => {
    vi.mocked(useAuth).mockReturnValue(
      createMockAuth({
        user: {
          id: 'user-dual-1',
          email: 'manager@campus.internal',
          roles: ['FacilityAdmin'],
          mfaCompleted: false,
        },
      })
    );

    const selfRequest: BookingRequestDto = {
      id: 'req-self-1',
      venueId: 'venue-jg-hall',
      venueName: 'Civil Auditorium (J.G. Hall)',
      venueCode: 'JG-HALL',
      requesterId: 'user-dual-1', // SAME AS LOGGED IN USER
      requesterEmail: 'manager@campus.internal',
      requesterName: 'Manager',
      eventTitle: 'Staff Meeting',
      description: null,
      expectedAttendance: 10,
      eventStartUtc: '2026-11-29T04:30:00Z',
      eventEndUtc: '2026-11-29T06:30:00Z',
      setupMinutes: 15,
      cleanupMinutes: 15,
      status: 'PendingReview',
      reviewNotes: null,
      conditions: null,
      conditionsAccepted: null,
      reviewedByUserId: null,
      reviewedByEmail: null,
      reviewedAtUtc: null,
      idempotencyKey: null,
      createdAtUtc: '2026-11-22T00:00:00Z',
      updatedAtUtc: '2026-11-22T00:00:00Z',
      notifications: [],
    };

    vi.spyOn(api, 'fetchReviewQueue').mockResolvedValue([selfRequest]);

    render(<VenuesScreen />);

    // Click Review Queue tab
    const reviewQueueBtn = screen.getByRole('button', { name: /Review Queue/i });
    fireEvent.click(reviewQueueBtn);

    await waitFor(() => {
      expect(screen.getByText('Staff Meeting')).toBeInTheDocument();
      expect(screen.getByText(/Self-Submission: Segregation of duties applies/i)).toBeInTheDocument();
    });

    // Action buttons must NOT be present for self-review
    expect(screen.queryByRole('button', { name: /^Approve$/i })).not.toBeInTheDocument();
  });

  it('allows FacilityAdmin assigned to venue to manage calendar and add blocks', async () => {
    const venuesWithStaffManager: VenueDto[] = [
      {
        ...mockVenues[0],
        managers: [
          {
            userId: 'assigned-staff-id',
            email: 'staff@campus.internal',
            displayName: 'Facility Staff',
            assignedAtUtc: '2026-11-20T00:00:00Z',
          },
        ],
      },
    ];
    vi.spyOn(api, 'fetchVenues').mockResolvedValue(venuesWithStaffManager);

    vi.mocked(useAuth).mockReturnValue(
      createMockAuth({
        user: {
          id: 'assigned-staff-id',
          email: 'staff@campus.internal',
          roles: ['FacilityAdmin'],
          mfaCompleted: false,
        },
      })
    );

    render(<VenuesScreen />);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Calendar & Rules/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /Calendar & Rules/i }));

    await waitFor(() => {
      expect(screen.getByText(/Authorized Facility Staff \/ Admin/i)).toBeInTheDocument();
      expect(screen.getByRole('heading', { name: /Add Calendar Block/i })).toBeInTheDocument();
    });
  });

  it('allows MFA-verified UniversityAdmin without venue assignment to view calendar management and add blocks', async () => {
    vi.mocked(useAuth).mockReturnValue(
      createMockAuth({
        user: {
          id: 'admin-user-1',
          email: 'admin@campus.internal',
          roles: ['UniversityAdmin'],
          mfaCompleted: true,
        },
      })
    );

    const addBlockSpy = vi.spyOn(api, 'addCalendarBlock').mockResolvedValue({
      id: 'block-new-1',
      venueId: 'venue-jg-hall',
      title: 'Emergency Maintenance',
      blockType: 'Maintenance',
      startUtc: '2026-11-25T03:30:00.000Z',
      endUtc: '2026-11-25T05:30:00.000Z',
      createdByUserId: 'admin-user-1',
      createdByUserEmail: 'admin@campus.internal',
      createdAtUtc: '2026-11-22T00:00:00.000Z',
    });

    render(<VenuesScreen />);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Calendar & Rules/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /Calendar & Rules/i }));

    await waitFor(() => {
      expect(screen.getByText(/Authorized Facility Staff \/ Admin/i)).toBeInTheDocument();
      expect(screen.getByRole('heading', { name: /Add Calendar Block/i })).toBeInTheDocument();
    });

    const titleInput = screen.getByPlaceholderText(/Block Title/i);
    fireEvent.change(titleInput, { target: { value: 'Emergency Maintenance' } });

    const submitBtn = screen.getByRole('button', { name: /^Add Calendar Block$/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(addBlockSpy).toHaveBeenCalledWith(
        'venue-jg-hall',
        expect.objectContaining({
          title: 'Emergency Maintenance',
          blockType: 'Class',
        })
      );
      expect(screen.getByText(/Calendar block "Emergency Maintenance" added successfully/i)).toBeInTheDocument();
    });
  });

  it('allows MFA-verified UniversityAdmin to search users and assign FacilityAdmin account as venue manager', async () => {
    vi.mocked(useAuth).mockReturnValue(
      createMockAuth({
        user: {
          id: 'admin-user-1',
          email: 'admin@campus.internal',
          roles: ['UniversityAdmin'],
          mfaCompleted: true,
        },
      })
    );

    const updatedVenueList: VenueDto[] = [
      {
        ...mockVenues[0],
        managers: [
          {
            userId: 'facility-user-1',
            email: 'facility@campus.internal',
            displayName: 'Facility Staff',
            assignedAtUtc: '2026-11-22T00:00:00Z',
          },
        ],
      },
      mockVenues[1],
    ];

    let currentVenues = mockVenues;
    vi.spyOn(api, 'fetchVenues').mockImplementation(async () => currentVenues);

    const assignSpy = vi.spyOn(api, 'assignVenueManager').mockImplementation(async () => {
      currentVenues = updatedVenueList;
      return {
        message: "User 'facility@campus.internal' assigned to manage venue 'Civil Auditorium (J.G. Hall)'.",
      };
    });

    render(<VenuesScreen />);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Calendar & Rules/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /Calendar & Rules/i }));

    // Verify Venue Managers section is visible with 0 managers
    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /Venue Managers/i })).toBeInTheDocument();
      expect(screen.getByText(/No managers currently assigned to this venue/i)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /Assign Venue Manager/i })).toBeInTheDocument();
    });

    // Open Assign Modal
    fireEvent.click(screen.getByRole('button', { name: /Assign Venue Manager/i }));

    // Verify Modal Header and Target Venue
    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /^Assign Venue Manager$/i })).toBeInTheDocument();
      expect(screen.getByText(/Target Venue:/i)).toBeInTheDocument();
    });

    // Search input exists and results are loaded
    const searchInput = screen.getByPlaceholderText(/Search user by email/i);
    fireEvent.change(searchInput, { target: { value: 'facility' } });
    fireEvent.click(screen.getByRole('button', { name: /^Search$/i }));

    await waitFor(() => {
      expect(screen.getByText('facility@campus.internal')).toBeInTheDocument();
      expect(screen.getByText('coordinator@campus.internal')).toBeInTheDocument();
      expect(screen.getByText(/Requires FacilityAdmin/i)).toBeInTheDocument();
    });

    // Select the eligible FacilityAdmin account
    const selectBtn = screen.getByRole('button', { name: /^Select$/i });
    fireEvent.click(selectBtn);

    // Verify Assignment Preview displays both selected venue and account
    await waitFor(() => {
      expect(screen.getByText(/Assignment Preview/i)).toBeInTheDocument();
      expect(screen.getByText(/Selected Venue:/i)).toBeInTheDocument();
      expect(screen.getByText(/Selected Account:/i)).toBeInTheDocument();
    });

    // Submit assignment
    const assignBtn = screen.getByRole('button', { name: /^Assign Manager$/i });
    fireEvent.click(assignBtn);

    // Verify API called and success state
    await waitFor(() => {
      expect(assignSpy).toHaveBeenCalledWith('venue-jg-hall', {
        userId: 'facility-user-1',
      });
      expect(screen.getByText(/User 'facility@campus.internal' assigned to manage venue/i)).toBeInTheDocument();
    });
  });

  it('hides the venue manager assignment controls from FacilityAdmin users', async () => {
    vi.mocked(useAuth).mockReturnValue(
      createMockAuth({
        user: {
          id: 'facility-staff-1',
          email: 'staff@campus.internal',
          roles: ['FacilityAdmin'],
          mfaCompleted: true,
        },
      })
    );

    render(<VenuesScreen />);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Calendar & Rules/i })).toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole('button', { name: /Calendar & Rules/i }));

    await waitFor(() => {
      expect(screen.getByText(/Calendar & Blocks/i)).toBeInTheDocument();
    });

    // Venue Managers section and button must be hidden for non-UniversityAdmin
    expect(screen.queryByRole('heading', { name: /Venue Managers/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Assign Venue Manager/i })).not.toBeInTheDocument();
  });

  it('hides the venue manager assignment controls from UniversityAdmin users without MFA', async () => {
    vi.mocked(useAuth).mockReturnValue(
      createMockAuth({
        user: {
          id: 'admin-no-mfa',
          email: 'admin@campus.internal',
          roles: ['UniversityAdmin'],
          mfaCompleted: false,
        },
      })
    );

    render(<VenuesScreen />);

    await waitFor(() => {
      expect(screen.getByText('Civil Auditorium (J.G. Hall)')).toBeInTheDocument();
    });

    // Tab itself is hidden without MFA session
    expect(screen.queryByRole('button', { name: /Calendar & Rules/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Assign Venue Manager/i })).not.toBeInTheDocument();
  });

  it('hides the venue manager assignment controls from EventCoordinator users', async () => {
    vi.mocked(useAuth).mockReturnValue(
      createMockAuth({
        user: {
          id: 'coord-1',
          email: 'coordinator@campus.internal',
          roles: ['EventCoordinator'],
          mfaCompleted: false,
        },
      })
    );

    render(<VenuesScreen />);

    await waitFor(() => {
      expect(screen.getByText('Civil Auditorium (J.G. Hall)')).toBeInTheDocument();
    });

    expect(screen.queryByRole('button', { name: /Calendar & Rules/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Assign Venue Manager/i })).not.toBeInTheDocument();
  });

  it('renders "Awaiting Confirmation" request and allows coordinator to confirm booking with reference displayed', async () => {
    const mockAwaitingReq: BookingRequestDto = {
      id: 'req-awaiting-1',
      venueId: 'venue-jg-hall',
      venueName: 'Civil Auditorium (J.G. Hall)',
      venueCode: 'JG-HALL',
      requesterId: 'user-coord-1',
      requesterEmail: 'coordinator@campus.internal',
      requesterName: 'Coordinator',
      eventTitle: 'Robotics Workshop 2026',
      description: null,
      expectedAttendance: 100,
      eventStartUtc: '2026-11-28T04:30:00Z',
      eventEndUtc: '2026-11-28T08:30:00Z',
      setupMinutes: 45,
      cleanupMinutes: 30,
      status: 'AwaitingConfirmation',
      reviewNotes: 'Approved by custodian',
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

    const mockConfirmedReq: BookingRequestDto = {
      ...mockAwaitingReq,
      status: 'Confirmed',
      reservationReference: 'RES-20261122-A1B2C3',
      confirmedReservationId: 'res-1',
      confirmedAtUtc: '2026-11-22T03:00:00Z',
    };

    vi.spyOn(api, 'fetchMyBookingRequests')
      .mockResolvedValueOnce([mockAwaitingReq])
      .mockResolvedValueOnce([mockConfirmedReq]);

    const confirmSpy = vi.spyOn(api, 'confirmBookingRequest').mockResolvedValue(mockConfirmedReq);

    render(<VenuesScreen />);

    // Click My Requests tab
    const myReqBtn = screen.getByRole('button', { name: /My Requests/i });
    fireEvent.click(myReqBtn);

    await waitFor(() => {
      expect(screen.getByText('Robotics Workshop 2026')).toBeInTheDocument();
      expect(screen.getByText(/Awaiting Confirmation/i)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /Confirm Booking/i })).toBeInTheDocument();
    });

    // Click Confirm Booking
    fireEvent.click(screen.getByRole('button', { name: /Confirm Booking/i }));

    await waitFor(() => {
      expect(confirmSpy).toHaveBeenCalledWith('req-awaiting-1');
      expect(screen.getByText(/Booking officially confirmed! Reservation Reference: RES-20261122-A1B2C3/i)).toBeInTheDocument();
    });
  });

  it('renders conflict error and leaves request in AwaitingConfirmation when booking confirmation detects unavailability', async () => {
    const mockAwaitingReq: BookingRequestDto = {
      id: 'req-awaiting-conflict',
      venueId: 'venue-jg-hall',
      venueName: 'Civil Auditorium (J.G. Hall)',
      venueCode: 'JG-HALL',
      requesterId: 'user-coord-1',
      requesterEmail: 'coordinator@campus.internal',
      requesterName: 'Coordinator',
      eventTitle: 'Debate Finals',
      description: null,
      expectedAttendance: 100,
      eventStartUtc: '2026-11-28T04:30:00Z',
      eventEndUtc: '2026-11-28T08:30:00Z',
      setupMinutes: 45,
      cleanupMinutes: 30,
      status: 'AwaitingConfirmation',
      reviewNotes: null,
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

    vi.spyOn(api, 'fetchMyBookingRequests').mockResolvedValue([mockAwaitingReq]);
    vi.spyOn(api, 'confirmBookingRequest').mockRejectedValue(
      new Error('Conflict detected: Venue is already booked for another event.')
    );

    render(<VenuesScreen />);

    // Click My Requests tab
    fireEvent.click(screen.getByRole('button', { name: /My Requests/i }));

    await waitFor(() => {
      expect(screen.getByText('Debate Finals')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /Confirm Booking/i })).toBeInTheDocument();
    });

    // Click Confirm Booking
    fireEvent.click(screen.getByRole('button', { name: /Confirm Booking/i }));

    await waitFor(() => {
      expect(screen.getByText(/Conflict detected: Venue is already booked/i)).toBeInTheDocument();
      // Must stay in AwaitingConfirmation!
      expect(screen.getByText(/Awaiting Confirmation/i)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /Confirm Booking/i })).toBeInTheDocument();
    });
  });

  /* =========================================================================
     PHASE 2.1: BUILDING DIRECTORY & VENUE ASSIGNMENT TESTS
     ========================================================================= */

  it('displays building badges on assigned venue and Unassigned on legacy unassigned venue in catalog', async () => {
    render(<VenuesScreen />);

    await waitFor(() => {
      expect(screen.getByText('Civil Auditorium (J.G. Hall)')).toBeInTheDocument();
      expect(screen.getByText('Faculty of Business Room 101')).toBeInTheDocument();
    });

    // Check building badges
    expect(screen.getByTestId('venue-building-venue-jg-hall')).toHaveTextContent('Civil Engineering Complex (BLD-CIVIL)');
    expect(screen.getByTestId('venue-building-venue-fob-101')).toHaveTextContent('Unassigned');
  });

  it('filters venue catalog by building (All Buildings default, Unassigned, specific building)', async () => {
    render(<VenuesScreen />);

    await waitFor(() => {
      expect(screen.getByText('Civil Auditorium (J.G. Hall)')).toBeInTheDocument();
      expect(screen.getByText('Faculty of Business Room 101')).toBeInTheDocument();
    });

    const filterSelect = screen.getByTestId('building-filter');
    expect(filterSelect).toHaveValue('all');

    // Filter by Unassigned
    fireEvent.change(filterSelect, { target: { value: 'unassigned' } });
    expect(screen.queryByText('Civil Auditorium (J.G. Hall)')).not.toBeInTheDocument();
    expect(screen.getByText('Faculty of Business Room 101')).toBeInTheDocument();

    // Filter by specific building (bld-civil)
    fireEvent.change(filterSelect, { target: { value: 'bld-civil' } });
    expect(screen.getByText('Civil Auditorium (J.G. Hall)')).toBeInTheDocument();
    expect(screen.queryByText('Faculty of Business Room 101')).not.toBeInTheDocument();

    // Filter by bld-fob (no venues assigned)
    fireEvent.change(filterSelect, { target: { value: 'bld-fob' } });
    expect(screen.queryByText('Civil Auditorium (J.G. Hall)')).not.toBeInTheDocument();
    expect(screen.queryByText('Faculty of Business Room 101')).not.toBeInTheDocument();
    expect(screen.getByText(/No venues match the selected building filter/i)).toBeInTheDocument();

    // Back to All Buildings
    fireEvent.change(filterSelect, { target: { value: 'all' } });
    expect(screen.getByText('Civil Auditorium (J.G. Hall)')).toBeInTheDocument();
    expect(screen.getByText('Faculty of Business Room 101')).toBeInTheDocument();
  });

  it('does not display Building Directory subview to non-admin coordinators', async () => {
    render(<VenuesScreen />);

    await waitFor(() => {
      expect(screen.getByText('Civil Auditorium (J.G. Hall)')).toBeInTheDocument();
    });

    // Coordinator cannot see Calendar & Rules tab or Building Directory
    expect(screen.queryByText(/Calendar & Rules/i)).not.toBeInTheDocument();
    expect(screen.queryByTestId('tab-subview-buildings')).not.toBeInTheDocument();
  });

  it('allows MFA-verified UniversityAdmin to view Building Directory and create a new building', async () => {
    const { useAuth } = await import('../auth/useAuth');
    vi.mocked(useAuth).mockReturnValue(
      createMockAuth({
        user: {
          id: 'admin-user-1',
          email: 'admin@campus.internal',
          roles: ['UniversityAdmin'],
          mfaCompleted: true,
        },
      })
    );

    const createSpy = vi.spyOn(api, 'createBuilding').mockResolvedValue({
      id: 'bld-arch',
      name: 'Faculty of Architecture',
      code: 'BLD-ARCH',
      venueCount: 0,
      createdAtUtc: '2026-11-22T00:00:00Z',
      updatedAtUtc: '2026-11-22T00:00:00Z',
    });

    render(<VenuesScreen />);

    // Switch to Calendar & Rules management tab
    await waitFor(() => {
      expect(screen.getByText(/Calendar & Rules/i)).toBeInTheDocument();
    });
    fireEvent.click(screen.getByText(/Calendar & Rules/i));

    // Click Building Directory subview button
    await waitFor(() => {
      expect(screen.getByTestId('tab-subview-buildings')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('tab-subview-buildings'));

    // Verify building directory table
    expect(screen.getByText('University Buildings Directory')).toBeInTheDocument();
    expect(screen.getByText('Civil Engineering Complex')).toBeInTheDocument();
    expect(screen.getByText('Faculty of Business Tower')).toBeInTheDocument();

    // Open Add Building modal
    fireEvent.click(screen.getByTestId('open-create-building-modal'));
    expect(screen.getByTestId('create-building-modal')).toBeInTheDocument();

    // Fill in building details
    fireEvent.change(screen.getByTestId('create-building-name'), {
      target: { value: 'Faculty of Architecture' },
    });
    fireEvent.change(screen.getByTestId('create-building-code'), {
      target: { value: 'BLD-ARCH' },
    });

    // Submit form
    fireEvent.click(screen.getByTestId('submit-create-building'));

    await waitFor(() => {
      expect(createSpy).toHaveBeenCalledWith({
        name: 'Faculty of Architecture',
        code: 'BLD-ARCH',
      });
      expect(screen.queryByTestId('create-building-modal')).not.toBeInTheDocument();
    });
  });

  it('displays 409 conflict error when creating building with duplicate code', async () => {
    const { useAuth } = await import('../auth/useAuth');
    vi.mocked(useAuth).mockReturnValue(
      createMockAuth({
        user: {
          id: 'admin-user-1',
          email: 'admin@campus.internal',
          roles: ['UniversityAdmin'],
          mfaCompleted: true,
        },
      })
    );

    vi.spyOn(api, 'createBuilding').mockRejectedValue(
      new Error("A building with code 'BLD-CIVIL' already exists.")
    );

    render(<VenuesScreen />);

    fireEvent.click(screen.getByText(/Calendar & Rules/i));
    await waitFor(() => {
      expect(screen.getByTestId('tab-subview-buildings')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('tab-subview-buildings'));

    fireEvent.click(screen.getByTestId('open-create-building-modal'));
    fireEvent.change(screen.getByTestId('create-building-name'), {
      target: { value: 'Civil Engineering Annex' },
    });
    fireEvent.change(screen.getByTestId('create-building-code'), {
      target: { value: 'BLD-CIVIL' },
    });

    fireEvent.click(screen.getByTestId('submit-create-building'));

    await waitFor(() => {
      expect(screen.getByTestId('create-building-error')).toHaveTextContent(
        "A building with code 'BLD-CIVIL' already exists."
      );
    });
  });

  it('allows MFA-verified UniversityAdmin to edit an existing building', async () => {
    const { useAuth } = await import('../auth/useAuth');
    vi.mocked(useAuth).mockReturnValue(
      createMockAuth({
        user: {
          id: 'admin-user-1',
          email: 'admin@campus.internal',
          roles: ['UniversityAdmin'],
          mfaCompleted: true,
        },
      })
    );

    const updateSpy = vi.spyOn(api, 'updateBuilding').mockResolvedValue({
      id: 'bld-civil',
      name: 'Civil Engineering & Environmental Complex',
      code: 'BLD-CIVIL-ENV',
      venueCount: 1,
      createdAtUtc: '2026-11-20T00:00:00Z',
      updatedAtUtc: '2026-11-22T00:00:00Z',
    });

    render(<VenuesScreen />);

    fireEvent.click(screen.getByText(/Calendar & Rules/i));
    await waitFor(() => {
      expect(screen.getByTestId('tab-subview-buildings')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('tab-subview-buildings'));

    // Click Edit on Civil Engineering Complex
    fireEvent.click(screen.getByTestId('edit-building-btn-bld-civil'));
    expect(screen.getByTestId('edit-building-modal')).toBeInTheDocument();
    expect(screen.getByTestId('edit-building-name')).toHaveValue('Civil Engineering Complex');
    expect(screen.getByTestId('edit-building-code')).toHaveValue('BLD-CIVIL');

    // Change details
    fireEvent.change(screen.getByTestId('edit-building-name'), {
      target: { value: 'Civil Engineering & Environmental Complex' },
    });
    fireEvent.change(screen.getByTestId('edit-building-code'), {
      target: { value: 'BLD-CIVIL-ENV' },
    });

    fireEvent.click(screen.getByTestId('submit-edit-building'));

    await waitFor(() => {
      expect(updateSpy).toHaveBeenCalledWith('bld-civil', {
        name: 'Civil Engineering & Environmental Complex',
        code: 'BLD-CIVIL-ENV',
      });
      expect(screen.queryByTestId('edit-building-modal')).not.toBeInTheDocument();
    });
  });

  it('allows MFA-verified UniversityAdmin to assign and unassign a venue building', async () => {
    const { useAuth } = await import('../auth/useAuth');
    vi.mocked(useAuth).mockReturnValue(
      createMockAuth({
        user: {
          id: 'admin-user-1',
          email: 'admin@campus.internal',
          roles: ['UniversityAdmin'],
          mfaCompleted: true,
        },
      })
    );

    const assignSpy = vi.spyOn(api, 'assignVenueBuilding').mockResolvedValue({
      ...mockVenues[0],
      buildingId: 'bld-fob',
      buildingName: 'Faculty of Business Tower',
      buildingCode: 'BLD-FOB',
    });

    render(<VenuesScreen />);

    fireEvent.click(screen.getByText(/Calendar & Rules/i));

    await waitFor(() => {
      expect(screen.getByText(/Building Assignment/i)).toBeInTheDocument();
    });

    const assignSelect = screen.getByTestId('assign-building-select');
    expect(assignSelect).toHaveValue('bld-civil');

    // Change to bld-fob
    fireEvent.change(assignSelect, { target: { value: 'bld-fob' } });
    fireEvent.click(screen.getByTestId('submit-assign-building'));

    await waitFor(() => {
      expect(assignSpy).toHaveBeenCalledWith('venue-jg-hall', { buildingId: 'bld-fob' });
    });

    // Test Unassign
    assignSpy.mockResolvedValueOnce({
      ...mockVenues[0],
      buildingId: null,
      buildingName: null,
      buildingCode: null,
    });

    fireEvent.change(assignSelect, { target: { value: '' } });
    fireEvent.click(screen.getByTestId('submit-assign-building'));

    await waitFor(() => {
      expect(assignSpy).toHaveBeenCalledWith('venue-jg-hall', { buildingId: null });
    });
  });
});
