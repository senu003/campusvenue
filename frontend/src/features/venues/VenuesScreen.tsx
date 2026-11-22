import React, { useState, useEffect } from 'react';
import type {
  VenueDto,
  CheckAvailabilityResponse,
  VenueCalendarCoverageDto,
  VenueCalendarBlockDto,
  BookingRequestDto,
  UpdateVenueRulesPayload,
} from './types';
import {
  fetchVenues,
  checkVenueAvailability,
  fetchCalendarCoverages,
  addCalendarCoverage,
  fetchCalendarBlocks,
  addCalendarBlock,
  deleteCalendarBlock,
  submitBookingRequest,
  fetchMyBookingRequests,
  fetchReviewQueue,
  reviewBookingRequest,
  respondToConditions,
  updateVenueRules,
  assignVenueManager,
} from './api';
import { fetchUserDirectory } from '../users/api';
import type { UserDirectoryItem } from '../users/types';
import { useAuth } from '../auth/useAuth';
import {
  Building2,
  CalendarCheck,
  CalendarX,
  AlertTriangle,
  Clock,
  Users,
  ShieldCheck,
  CheckCircle2,
  Calendar,
  Layers,
  Trash2,
  Plus,
  RefreshCw,
  Send,
  FileText,
  Check,
  X,
  AlertCircle,
  Settings,
  ClipboardList,
  UserPlus,
  Search,
} from 'lucide-react';

export const VenuesScreen: React.FC = () => {
  const { user } = useAuth();
  const [activeTab, setActiveTab] = useState<'catalog' | 'my-requests' | 'review-queue' | 'management'>('catalog');

  // Venues state
  const [venues, setVenues] = useState<VenueDto[]>([]);
  const [selectedVenueId, setSelectedVenueId] = useState<string>('');
  const [loadingVenues, setLoadingVenues] = useState<boolean>(true);
  const [venuesError, setVenuesError] = useState<string | null>(null);

  // Availability form state
  const [eventDate, setEventDate] = useState<string>(() => {
    const d = new Date();
    d.setDate(d.getDate() + 5);
    return d.toISOString().split('T')[0];
  });
  const [startTime, setStartTime] = useState<string>('09:00');
  const [endTime, setEndTime] = useState<string>('11:00');
  const [setupMinutes, setSetupMinutes] = useState<number>(30);
  const [cleanupMinutes, setCleanupMinutes] = useState<number>(30);

  // Availability result state
  const [checking, setChecking] = useState<boolean>(false);
  const [availabilityResult, setAvailabilityResult] = useState<CheckAvailabilityResponse | null>(null);
  const [checkError, setCheckError] = useState<string | null>(null);

  // Booking request submission state
  const [showRequestModal, setShowRequestModal] = useState<boolean>(false);
  const [eventTitle, setEventTitle] = useState<string>('');
  const [eventDescription, setEventDescription] = useState<string>('');
  const [expectedAttendance, setExpectedAttendance] = useState<number>(50);
  const [rulesAcknowledged, setRulesAcknowledged] = useState<boolean>(false);
  const [submittingRequest, setSubmittingRequest] = useState<boolean>(false);
  const [submitSuccess, setSubmitSuccess] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);

  // My requests state
  const [myRequests, setMyRequests] = useState<BookingRequestDto[]>([]);
  const [loadingMyRequests, setLoadingMyRequests] = useState<boolean>(false);
  const [myRequestsError, setMyRequestsError] = useState<string | null>(null);
  const [actionSuccess, setActionSuccess] = useState<string | null>(null);

  // Manager review queue state
  const [reviewQueue, setReviewQueue] = useState<BookingRequestDto[]>([]);
  const [loadingReviewQueue, setLoadingReviewQueue] = useState<boolean>(false);
  const [reviewQueueError, setReviewQueueError] = useState<string | null>(null);

  // Review modal state
  const [selectedRequestForReview, setSelectedRequestForReview] = useState<BookingRequestDto | null>(null);
  const [reviewDecision, setReviewDecision] = useState<'Approve' | 'ApproveWithConditions' | 'Reject'>('Approve');
  const [reviewNotes, setReviewNotes] = useState<string>('');
  const [reviewConditions, setReviewConditions] = useState<string>('');
  const [submittingReview, setSubmittingReview] = useState<boolean>(false);
  const [reviewActionError, setReviewActionError] = useState<string | null>(null);

  // Management tab state
  const [coverages, setCoverages] = useState<VenueCalendarCoverageDto[]>([]);
  const [blocks, setBlocks] = useState<VenueCalendarBlockDto[]>([]);
  const [loadingManagement, setLoadingManagement] = useState<boolean>(false);
  const [managementError, setManagementError] = useState<string | null>(null);
  const [managementSuccess, setManagementSuccess] = useState<string | null>(null);
  const [managementSubView, setManagementSubView] = useState<'calendar' | 'rules'>('calendar');

  // Venue rules management form state
  const [ruleOpStart, setRuleOpStart] = useState<string>('08:00');
  const [ruleOpEnd, setRuleOpEnd] = useState<string>('20:00');
  const [ruleCapacity, setRuleCapacity] = useState<number>(100);
  const [ruleMinNotice, setRuleMinNotice] = useState<number>(24);
  const [ruleSetupMinutes, setRuleSetupMinutes] = useState<number>(30);
  const [ruleCleanupMinutes, setRuleCleanupMinutes] = useState<number>(30);
  const [ruleMaxBookingHours, setRuleMaxBookingHours] = useState<number>(8);
  const [ruleRequiresApproval, setRuleRequiresApproval] = useState<boolean>(true);
  const [ruleGeneralRules, setRuleGeneralRules] = useState<string>('');
  const [savingRules, setSavingRules] = useState<boolean>(false);

  // Add block form state
  const [blockTitle, setBlockTitle] = useState<string>('');
  const [blockType, setBlockType] = useState<string>('Class');
  const [blockStartDate, setBlockStartDate] = useState<string>(() => new Date().toISOString().split('T')[0]);
  const [blockStartTime, setBlockStartTime] = useState<string>('09:00');
  const [blockEndTime, setBlockEndTime] = useState<string>('11:00');

  // Add coverage form state
  const [coverageSource, setCoverageSource] = useState<string>('CITeS RBS Staff System (book.uom.lk)');
  const [coverageReference, setCoverageReference] = useState<string>('Semester 2 2026 Academic Term');
  const [coverageFromDate, setCoverageFromDate] = useState<string>(() => new Date().toISOString().split('T')[0]);
  const [coverageToDate, setCoverageToDate] = useState<string>(() => {
    const d = new Date();
    d.setDate(d.getDate() + 90);
    return d.toISOString().split('T')[0];
  });
  const [coverageNotes, setCoverageNotes] = useState<string>('');

  const isUniAdmin = Boolean(user?.roles?.includes('UniversityAdmin') && user?.mfaCompleted);
  const isFacilityStaff = Boolean(user?.roles?.includes('FacilityAdmin') || isUniAdmin);
  const isEventCoordinator = Boolean(user?.roles?.includes('EventCoordinator') || isUniAdmin);
  const canManageVenueManagers = isUniAdmin;

  // Venue Manager Assignment State (UniversityAdmin with MFA)
  const [showAssignModal, setShowAssignModal] = useState<boolean>(false);
  const [managerSearchEmail, setManagerSearchEmail] = useState<string>('');
  const [searchedUsers, setSearchedUsers] = useState<UserDirectoryItem[]>([]);
  const [loadingUserSearch, setLoadingUserSearch] = useState<boolean>(false);
  const [selectedUserForManager, setSelectedUserForManager] = useState<UserDirectoryItem | null>(null);
  const [isAssigningManager, setIsAssigningManager] = useState<boolean>(false);
  const [assignManagerError, setAssignManagerError] = useState<string | null>(null);

  const selectedVenue = venues.find((v) => v.id === selectedVenueId);

  // Is current user assigned to manage this venue?
  const isAssignedManager = Boolean(
    isUniAdmin || (user && selectedVenue?.managers?.some((m) => m.userId === user.id))
  );

  const refreshVenues = async () => {
    try {
      const data = await fetchVenues();
      setVenues(data);
      if (data.length > 0 && !selectedVenueId) {
        setSelectedVenueId(data[0].id);
        populateVenueRulesForm(data[0]);
      } else if (selectedVenueId) {
        const current = data.find((v) => v.id === selectedVenueId);
        if (current) populateVenueRulesForm(current);
      }
    } catch (err: unknown) {
      setVenuesError(err instanceof Error ? err.message : 'Failed to load venues.');
    } finally {
      setLoadingVenues(false);
    }
  };

  const populateVenueRulesForm = (venue: VenueDto) => {
    setRuleOpStart(venue.operatingHoursStart);
    setRuleOpEnd(venue.operatingHoursEnd);
    setRuleCapacity(venue.capacity);
    setRuleMinNotice(venue.minNoticeHours);
    setRuleSetupMinutes(venue.defaultSetupMinutes);
    setRuleCleanupMinutes(venue.defaultCleanupMinutes);
    setRuleMaxBookingHours(venue.maxBookingHours);
    setRuleRequiresApproval(venue.requiresEventApproval);
    setRuleGeneralRules(venue.generalRules || '');
  };

  const refreshManagementData = async (venueId: string) => {
    setLoadingManagement(true);
    try {
      const [covData, blockData] = await Promise.all([
        fetchCalendarCoverages(venueId),
        fetchCalendarBlocks(venueId),
      ]);
      setCoverages(covData);
      setBlocks(blockData);
    } catch (err: unknown) {
      setManagementError(err instanceof Error ? err.message : 'Failed to load calendar management data.');
    } finally {
      setLoadingManagement(false);
    }
  };

  const refreshMyRequests = async () => {
    setLoadingMyRequests(true);
    setMyRequestsError(null);
    try {
      const data = await fetchMyBookingRequests();
      setMyRequests(data);
    } catch (err: unknown) {
      setMyRequestsError(err instanceof Error ? err.message : 'Failed to load your booking requests.');
    } finally {
      setLoadingMyRequests(false);
    }
  };

  const refreshReviewQueue = async () => {
    setLoadingReviewQueue(true);
    setReviewQueueError(null);
    try {
      const data = await fetchReviewQueue();
      setReviewQueue(data);
    } catch (err: unknown) {
      setReviewQueueError(err instanceof Error ? err.message : 'Failed to load review queue.');
    } finally {
      setLoadingReviewQueue(false);
    }
  };

  useEffect(() => {
    let ignore = false;

    async function loadInitial() {
      try {
        const data = await fetchVenues();
        if (!ignore) {
          setVenues(data);
          if (data.length > 0) {
            setSelectedVenueId((prev) => prev || data[0].id);
            setSetupMinutes(data[0].defaultSetupMinutes);
            setCleanupMinutes(data[0].defaultCleanupMinutes);
            populateVenueRulesForm(data[0]);
          }
          setLoadingVenues(false);
        }
      } catch (err: unknown) {
        if (!ignore) {
          setVenuesError(err instanceof Error ? err.message : 'Failed to load venues.');
          setLoadingVenues(false);
        }
      }
    }

    loadInitial();

    return () => {
      ignore = true;
    };
  }, []);

  // Tab change handlers
  const handleTabChange = (tab: 'catalog' | 'my-requests' | 'review-queue' | 'management') => {
    setActiveTab(tab);
    if (tab === 'my-requests' && user) {
      refreshMyRequests();
    } else if (tab === 'review-queue' && isFacilityStaff) {
      refreshReviewQueue();
    } else if (tab === 'management' && selectedVenueId) {
      refreshManagementData(selectedVenueId);
    }
  };

  // Update buffers when venue selection changes
  const handleSelectVenue = (venue: VenueDto) => {
    setSelectedVenueId(venue.id);
    setSetupMinutes(venue.defaultSetupMinutes);
    setCleanupMinutes(venue.defaultCleanupMinutes);
    populateVenueRulesForm(venue);
    setAvailabilityResult(null);
    setCheckError(null);
  };

  // Check availability handler
  const handleCheckAvailability = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedVenue) return;

    setChecking(true);
    setCheckError(null);
    setAvailabilityResult(null);

    try {
      const startIso = `${eventDate}T${startTime}:00+05:30`;
      const endIso = `${eventDate}T${endTime}:00+05:30`;

      const startUtc = new Date(startIso).toISOString();
      const endUtc = new Date(endIso).toISOString();

      const result = await checkVenueAvailability(selectedVenue.id, {
        eventStartUtc: startUtc,
        eventEndUtc: endUtc,
        setupMinutes,
        cleanupMinutes,
      });

      setAvailabilityResult(result);
    } catch (err: unknown) {
      setCheckError(err instanceof Error ? err.message : 'Failed to check venue availability.');
    } finally {
      setChecking(false);
    }
  };

  // Submit booking request handler
  const handleSubmitBookingRequest = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedVenue || !availabilityResult) return;

    if (!rulesAcknowledged) {
      setSubmitError('Please acknowledge compliance with general venue rules before submitting.');
      return;
    }

    setSubmittingRequest(true);
    setSubmitError(null);
    setSubmitSuccess(null);

    try {
      await submitBookingRequest(selectedVenue.id, {
        eventTitle: eventTitle.trim(),
        description: eventDescription.trim() || undefined,
        expectedAttendance,
        eventStartUtc: availabilityResult.eventStartUtc,
        eventEndUtc: availabilityResult.eventEndUtc,
        setupMinutes: availabilityResult.setupMinutes,
        cleanupMinutes: availabilityResult.cleanupMinutes,
        idempotencyKey: `req-${Date.now()}-${Math.random().toString(36).substring(2, 9)}`,
      });

      setSubmitSuccess(`Booking request for "${eventTitle}" submitted successfully! Assigned venue managers have been notified.`);
      setShowRequestModal(false);
      setEventTitle('');
      setEventDescription('');
      setRulesAcknowledged(false);
      setAvailabilityResult(null);

      // Refresh requests if in view
      if (user) {
        refreshMyRequests();
      }
    } catch (err: unknown) {
      setSubmitError(err instanceof Error ? err.message : 'Failed to submit booking request.');
    } finally {
      setSubmittingRequest(false);
    }
  };

  // Save venue rules handler
  const handleSaveVenueRules = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedVenueId) return;

    setSavingRules(true);
    setManagementError(null);
    setManagementSuccess(null);

    try {
      const payload: UpdateVenueRulesPayload = {
        operatingHoursStart: ruleOpStart,
        operatingHoursEnd: ruleOpEnd,
        capacity: ruleCapacity,
        minNoticeHours: ruleMinNotice,
        defaultSetupMinutes: ruleSetupMinutes,
        defaultCleanupMinutes: ruleCleanupMinutes,
        maxBookingHours: ruleMaxBookingHours,
        requiresEventApproval: ruleRequiresApproval,
        generalRules: ruleGeneralRules.trim() || null,
      };

      const updated = await updateVenueRules(selectedVenueId, payload);
      setManagementSuccess(`Venue rules and policies for "${updated.name}" updated successfully.`);
      await refreshVenues();
    } catch (err: unknown) {
      setManagementError(err instanceof Error ? err.message : 'Failed to update venue rules.');
    } finally {
      setSavingRules(false);
    }
  };

  // Respond to conditions handler
  const handleRespondToConditions = async (requestId: string, accept: boolean) => {
    setActionSuccess(null);
    try {
      const updated = await respondToConditions(requestId, accept);
      setActionSuccess(
        accept
          ? `Conditions accepted for "${updated.eventTitle}". Request is now Awaiting Booking Confirmation.`
          : `Conditions declined. Booking request for "${updated.eventTitle}" has been cancelled.`
      );
      await refreshMyRequests();
    } catch (err: unknown) {
      setMyRequestsError(err instanceof Error ? err.message : 'Failed to update conditions response.');
    }
  };

  // Submit manager review handler
  const handleSubmitReview = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedRequestForReview) return;

    setSubmittingReview(true);
    setReviewActionError(null);

    try {
      await reviewBookingRequest(selectedRequestForReview.id, {
        decision: reviewDecision,
        notes: reviewNotes.trim() || undefined,
        conditions: reviewDecision === 'ApproveWithConditions' ? reviewConditions.trim() : undefined,
      });

      setSelectedRequestForReview(null);
      setReviewNotes('');
      setReviewConditions('');
      await refreshReviewQueue();
    } catch (err: unknown) {
      setReviewActionError(err instanceof Error ? err.message : 'Failed to submit review decision.');
    } finally {
      setSubmittingReview(false);
    }
  };

  // Add block handler
  const handleAddBlock = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedVenueId) return;

    setManagementError(null);
    setManagementSuccess(null);

    try {
      const startIso = `${blockStartDate}T${blockStartTime}:00+05:30`;
      const endIso = `${blockStartDate}T${blockEndTime}:00+05:30`;
      const startUtc = new Date(startIso).toISOString();
      const endUtc = new Date(endIso).toISOString();

      await addCalendarBlock(selectedVenueId, {
        title: blockTitle.trim(),
        blockType,
        startUtc,
        endUtc,
      });

      setManagementSuccess(`Calendar block "${blockTitle}" added successfully.`);
      setBlockTitle('');
      await refreshManagementData(selectedVenueId);
    } catch (err: unknown) {
      setManagementError(err instanceof Error ? err.message : 'Failed to add calendar block.');
    }
  };

  // Add coverage handler
  const handleAddCoverage = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedVenueId) return;

    setManagementError(null);
    setManagementSuccess(null);

    try {
      const fromUtc = new Date(`${coverageFromDate}T00:00:00Z`).toISOString();
      const toUtc = new Date(`${coverageToDate}T23:59:59Z`).toISOString();

      await addCalendarCoverage(selectedVenueId, {
        sourceName: coverageSource.trim(),
        sourceReference: coverageReference.trim() || undefined,
        coversFromUtc: fromUtc,
        coversToUtc: toUtc,
        notes: coverageNotes.trim() || undefined,
      });

      setManagementSuccess('Verified calendar coverage record created successfully.');
      await refreshManagementData(selectedVenueId);
    } catch (err: unknown) {
      setManagementError(err instanceof Error ? err.message : 'Failed to record coverage.');
    }
  };

  // Delete block handler
  const handleDeleteBlock = async (blockId: string, title: string) => {
    if (!selectedVenueId) return;
    if (!window.confirm(`Delete calendar block "${title}"?`)) return;

    try {
      await deleteCalendarBlock(selectedVenueId, blockId);
      setManagementSuccess(`Block "${title}" removed.`);
      await refreshManagementData(selectedVenueId);
    } catch (err: unknown) {
      setManagementError(err instanceof Error ? err.message : 'Failed to delete block.');
    }
  };

  // Venue Manager Assignment Handlers (UniversityAdmin with MFA)
  const handleOpenAssignModal = async () => {
    setShowAssignModal(true);
    setAssignManagerError(null);
    setSelectedUserForManager(null);
    setManagerSearchEmail('');
    setLoadingUserSearch(true);
    try {
      const response = await fetchUserDirectory({ pageSize: 20 });
      setSearchedUsers(response.items);
    } catch {
      setSearchedUsers([]);
    } finally {
      setLoadingUserSearch(false);
    }
  };

  const handleCloseAssignModal = () => {
    setShowAssignModal(false);
    setSelectedUserForManager(null);
    setAssignManagerError(null);
  };

  const handleSearchUsers = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    setLoadingUserSearch(true);
    setAssignManagerError(null);
    try {
      const response = await fetchUserDirectory({
        search: managerSearchEmail.trim() || undefined,
        pageSize: 20,
      });
      setSearchedUsers(response.items);
    } catch (err: unknown) {
      setAssignManagerError(err instanceof Error ? err.message : 'Failed to search users.');
      setSearchedUsers([]);
    } finally {
      setLoadingUserSearch(false);
    }
  };

  const handleAssignManager = async () => {
    if (!selectedVenue || !selectedUserForManager) return;
    setIsAssigningManager(true);
    setAssignManagerError(null);

    try {
      const res = await assignVenueManager(selectedVenue.id, {
        userId: selectedUserForManager.id,
      });
      await refreshVenues();
      setManagementSuccess(res.message || `User '${selectedUserForManager.email}' assigned to manage venue '${selectedVenue.name}'.`);
      setShowAssignModal(false);
      setSelectedUserForManager(null);
    } catch (err: unknown) {
      setAssignManagerError(err instanceof Error ? err.message : 'Failed to assign venue manager.');
    } finally {
      setIsAssigningManager(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Top Header & Overview */}
      <div className="flex flex-col md:flex-row md:items-center md:justify-between border-b pb-4 gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-gray-900 flex items-center gap-2">
            <Building2 className="h-6 w-6 text-indigo-600" />
            Venues & Booking Requests
          </h1>
          <p className="text-sm text-gray-500 mt-1">
            University venue catalog, general venue-use policies, live availability, and coordinator booking requests.
          </p>
        </div>

        {/* View Navigation Tabs */}
        <div className="flex flex-wrap rounded-md shadow-sm border border-gray-200 p-1 bg-gray-50 gap-1">
          <button
            type="button"
            onClick={() => handleTabChange('catalog')}
            className={`px-3 py-1.5 text-xs font-medium rounded-md transition-colors ${
              activeTab === 'catalog'
                ? 'bg-white text-indigo-700 shadow-sm'
                : 'text-gray-600 hover:text-gray-900'
            }`}
          >
            Catalog & Availability
          </button>
          {user && (
            <button
              type="button"
              onClick={() => handleTabChange('my-requests')}
              className={`px-3 py-1.5 text-xs font-medium rounded-md transition-colors flex items-center gap-1 ${
                activeTab === 'my-requests'
                  ? 'bg-white text-indigo-700 shadow-sm'
                  : 'text-gray-600 hover:text-gray-900'
              }`}
            >
              <ClipboardList className="h-3.5 w-3.5" />
              My Requests
            </button>
          )}
          {isFacilityStaff && (
            <button
              type="button"
              onClick={() => handleTabChange('review-queue')}
              className={`px-3 py-1.5 text-xs font-medium rounded-md transition-colors flex items-center gap-1 ${
                activeTab === 'review-queue'
                  ? 'bg-white text-indigo-700 shadow-sm'
                  : 'text-gray-600 hover:text-gray-900'
              }`}
            >
              <CalendarCheck className="h-3.5 w-3.5" />
              Review Queue
            </button>
          )}
          {isFacilityStaff && (
            <button
              type="button"
              onClick={() => handleTabChange('management')}
              className={`px-3 py-1.5 text-xs font-medium rounded-md transition-colors flex items-center gap-1 ${
                activeTab === 'management'
                  ? 'bg-white text-indigo-700 shadow-sm'
                  : 'text-gray-600 hover:text-gray-900'
              }`}
            >
              <Settings className="h-3.5 w-3.5" />
              Calendar & Rules
            </button>
          )}
        </div>
      </div>

      {/* Global Success / Info Banners */}
      {submitSuccess && (
        <div className="p-4 bg-emerald-50 border border-emerald-200 rounded-md text-xs text-emerald-800 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="h-4 w-4 text-emerald-600 shrink-0" />
            <span>{submitSuccess}</span>
          </div>
          <button
            type="button"
            onClick={() => {
              setActiveTab('my-requests');
              setSubmitSuccess(null);
            }}
            className="font-semibold underline ml-4 hover:text-emerald-950"
          >
            View My Requests &rarr;
          </button>
        </div>
      )}

      {/* Loading & Global Error States */}
      {loadingVenues && (
        <div className="flex items-center justify-center p-12 text-sm text-gray-500 gap-2">
          <RefreshCw className="h-5 w-5 animate-spin text-indigo-600" />
          Loading university venues...
        </div>
      )}

      {venuesError && (
        <div className="p-4 bg-red-50 border border-red-200 rounded-md text-sm text-red-700 flex items-center gap-2">
          <AlertTriangle className="h-5 w-5 text-red-500" />
          {venuesError}
        </div>
      )}

      {!loadingVenues && !venuesError && (
        <>
          {/* =========================================================================
              TAB 1: CATALOG & AVAILABILITY CHECK & BOOKING SUBMISSION
              ========================================================================= */}
          {activeTab === 'catalog' && (
            <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
              {/* Venue List Column */}
              <div className="lg:col-span-5 space-y-4">
                <h2 className="text-base font-semibold text-gray-900 flex items-center gap-2">
                  <Layers className="h-4 w-4 text-gray-500" />
                  Select Venue
                </h2>

                <div className="space-y-3">
                  {venues.map((venue) => {
                    const isSelected = venue.id === selectedVenueId;
                    return (
                      <div
                        key={venue.id}
                        onClick={() => handleSelectVenue(venue)}
                        className={`p-4 rounded-lg border cursor-pointer transition-all ${
                          isSelected
                            ? 'border-indigo-600 bg-indigo-50/40 shadow-sm'
                            : 'border-gray-200 bg-white hover:border-gray-300 hover:bg-gray-50'
                        }`}
                      >
                        <div className="flex items-start justify-between">
                          <div>
                            <div className="flex items-center gap-2">
                              <h3 className="text-sm font-bold text-gray-900">{venue.name}</h3>
                              <span
                                className={`text-[10px] font-semibold px-2 py-0.5 rounded-full ${
                                  venue.type === 'Central'
                                    ? 'bg-purple-100 text-purple-800'
                                    : 'bg-blue-100 text-blue-800'
                                }`}
                              >
                                {venue.type} Managed
                              </span>
                            </div>
                            <p className="text-xs text-gray-500 mt-0.5">{venue.managingUnit}</p>
                          </div>
                          <span className="font-mono text-xs font-semibold text-gray-400">{venue.code}</span>
                        </div>

                        <div className="mt-3 grid grid-cols-3 gap-2 text-xs text-gray-600 border-t pt-2 border-gray-100">
                          <div>
                            <span className="text-gray-400 block text-[10px]">Capacity</span>
                            <span className="font-medium flex items-center gap-1">
                              <Users className="h-3 w-3 text-gray-400" />
                              {venue.capacity}
                            </span>
                          </div>
                          <div>
                            <span className="text-gray-400 block text-[10px]">Hours</span>
                            <span className="font-medium flex items-center gap-1">
                              <Clock className="h-3 w-3 text-gray-400" />
                              {venue.operatingHoursStart}–{venue.operatingHoursEnd}
                            </span>
                          </div>
                          <div>
                            <span className="text-gray-400 block text-[10px]">Buffers</span>
                            <span className="font-medium">
                              +{venue.defaultSetupMinutes}m / +{venue.defaultCleanupMinutes}m
                            </span>
                          </div>
                        </div>

                        {/* General Rules Excerpt */}
                        {venue.generalRules && (
                          <div className="mt-2.5 p-2 bg-gray-50 rounded border border-gray-100 text-[11px] text-gray-600">
                            <span className="font-semibold text-gray-700 block mb-0.5 flex items-center gap-1">
                              <FileText className="h-3 w-3 text-indigo-500" />
                              General Venue Rules:
                            </span>
                            <p className="line-clamp-2 whitespace-pre-line text-gray-600">{venue.generalRules}</p>
                          </div>
                        )}

                        {venue.requiresEventApproval && (
                          <div className="mt-2 text-[11px] text-purple-700 bg-purple-50 px-2 py-0.5 rounded flex items-center gap-1">
                            <ShieldCheck className="h-3.5 w-3.5" />
                            Requires Student Welfare event approval
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
              </div>

              {/* Availability Form & Result Column */}
              <div className="lg:col-span-7 space-y-6">
                {selectedVenue && (
                  <div className="bg-white rounded-lg border border-gray-200 shadow-sm p-6 space-y-6">
                    <div>
                      <h2 className="text-base font-semibold text-gray-900 flex items-center gap-2">
                        <CalendarCheck className="h-5 w-5 text-indigo-600" />
                        Check Availability for {selectedVenue.name}
                      </h2>
                      <p className="text-xs text-gray-500 mt-1">
                        Select your event window. We calculate the complete occupied interval including setup
                        and cleanup buffers, evaluating verified calendar coverage and timetable blocks.
                      </p>
                    </div>

                    <form onSubmit={handleCheckAvailability} className="space-y-4">
                      {/* Date & Time Row */}
                      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                        <div>
                          <label className="block text-xs font-medium text-gray-700 mb-1">Event Date</label>
                          <input
                            type="date"
                            value={eventDate}
                            onChange={(e) => setEventDate(e.target.value)}
                            className="w-full text-xs rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
                            required
                          />
                        </div>
                        <div>
                          <label className="block text-xs font-medium text-gray-700 mb-1">Event Start (Local)</label>
                          <input
                            type="time"
                            value={startTime}
                            onChange={(e) => setStartTime(e.target.value)}
                            className="w-full text-xs rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
                            required
                          />
                        </div>
                        <div>
                          <label className="block text-xs font-medium text-gray-700 mb-1">Event End (Local)</label>
                          <input
                            type="time"
                            value={endTime}
                            onChange={(e) => setEndTime(e.target.value)}
                            className="w-full text-xs rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
                            required
                          />
                        </div>
                      </div>

                      {/* Buffer Row */}
                      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 p-3 bg-gray-50 rounded-md border border-gray-100">
                        <div>
                          <label className="block text-xs font-medium text-gray-700 mb-1">
                            Setup Buffer (Minutes)
                          </label>
                          <input
                            type="number"
                            min="0"
                            max="240"
                            value={setupMinutes}
                            onChange={(e) => setSetupMinutes(parseInt(e.target.value, 10) || 0)}
                            className="w-full text-xs rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
                          />
                          <span className="text-[10px] text-gray-500">Venue default: {selectedVenue.defaultSetupMinutes}m</span>
                        </div>
                        <div>
                          <label className="block text-xs font-medium text-gray-700 mb-1">
                            Cleanup Buffer (Minutes)
                          </label>
                          <input
                            type="number"
                            min="0"
                            max="240"
                            value={cleanupMinutes}
                            onChange={(e) => setCleanupMinutes(parseInt(e.target.value, 10) || 0)}
                            className="w-full text-xs rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
                          />
                          <span className="text-[10px] text-gray-500">Venue default: {selectedVenue.defaultCleanupMinutes}m</span>
                        </div>
                      </div>

                      {/* Notice & Rule Indicator */}
                      <div className="text-[11px] text-gray-500 flex items-center justify-between border-t pt-2">
                        <span>Notice requirement: min {selectedVenue.minNoticeHours}h advance notice</span>
                        <span>Operating window: {selectedVenue.operatingHoursStart} - {selectedVenue.operatingHoursEnd}</span>
                      </div>

                      <button
                        type="submit"
                        disabled={checking}
                        className="w-full inline-flex justify-center items-center py-2 px-4 border border-transparent shadow-sm text-xs font-medium rounded-md text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50"
                      >
                        {checking ? (
                          <>
                            <RefreshCw className="h-4 w-4 animate-spin mr-2" />
                            Evaluating availability...
                          </>
                        ) : (
                          <>
                            <CheckCircle2 className="h-4 w-4 mr-1.5" />
                            Check Availability
                          </>
                        )}
                      </button>
                    </form>

                    {/* Check Error Notice */}
                    {checkError && (
                      <div className="p-3 bg-red-50 border border-red-200 rounded-md text-xs text-red-700 flex items-center gap-2">
                        <AlertTriangle className="h-4 w-4 text-red-500 shrink-0" />
                        {checkError}
                      </div>
                    )}

                    {/* Availability Result Display */}
                    {availabilityResult && (
                      <div
                        className={`p-4 rounded-lg border space-y-4 ${
                          availabilityResult.status === 'Available'
                            ? 'bg-emerald-50/50 border-emerald-200'
                            : availabilityResult.status === 'Unavailable'
                            ? 'bg-red-50/50 border-red-200'
                            : 'bg-amber-50/50 border-amber-200'
                        }`}
                      >
                        {/* Status Header Badge */}
                        <div className="flex items-center justify-between">
                          <span
                            className={`text-xs font-bold px-2.5 py-1 rounded-full uppercase tracking-wider ${
                              availabilityResult.status === 'Available'
                                ? 'bg-emerald-100 text-emerald-800'
                                : availabilityResult.status === 'Unavailable'
                                ? 'bg-red-100 text-red-800'
                                : 'bg-amber-100 text-amber-800'
                            }`}
                          >
                            {availabilityResult.status}
                          </span>
                          <span className="text-[11px] text-gray-500">
                            Occupied Window: {new Date(availabilityResult.occupiedStartUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} -{' '}
                            {new Date(availabilityResult.occupiedEndUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                          </span>
                        </div>

                        {/* Reason Explanation */}
                        <p
                          className={`text-xs font-medium ${
                            availabilityResult.status === 'Available'
                              ? 'text-emerald-950'
                              : availabilityResult.status === 'Unavailable'
                              ? 'text-red-950'
                              : 'text-amber-950'
                          }`}
                        >
                          {availabilityResult.reason}
                        </p>

                        {/* Conflict Details (if any) */}
                        {availabilityResult.conflictDetails && (
                          <div className="p-3 bg-white/80 rounded border border-red-200 text-xs text-gray-700 space-y-1">
                            <div className="flex items-center justify-between">
                              <span className="font-semibold text-red-700 flex items-center gap-1">
                                <CalendarX className="h-3.5 w-3.5" />
                                Conflicting Block: {availabilityResult.conflictDetails.title}
                              </span>
                              <span className="text-[10px] bg-red-100 text-red-800 px-1.5 py-0.5 rounded">
                                {availabilityResult.conflictDetails.blockType}
                              </span>
                            </div>
                            <p className="text-[11px] text-gray-600">
                              Scheduled:{' '}
                              {new Date(availabilityResult.conflictDetails.startUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} -{' '}
                              {new Date(availabilityResult.conflictDetails.endUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} UTC
                            </p>
                            {availabilityResult.conflictDetails.isBufferConflictOnly && (
                              <p className="text-[11px] text-amber-700 font-medium">
                                Note: Event window itself is free, but the requested {availabilityResult.conflictDetails.conflictScope === 'SetupBuffer' ? 'setup' : 'cleanup'} buffer overlaps with this block.
                              </p>
                            )}
                          </div>
                        )}

                        {/* Verified Coverage Details (if verified) */}
                        {availabilityResult.coverageDetails && (
                          <div className="text-[11px] text-gray-600 bg-white/60 p-2.5 rounded border border-gray-200 space-y-0.5">
                            <span className="font-semibold text-gray-800 flex items-center gap-1">
                              <ShieldCheck className="h-3.5 w-3.5 text-emerald-600" />
                              Verified Timetable Source: {availabilityResult.coverageDetails.sourceName}
                            </span>
                            {availabilityResult.coverageDetails.sourceReference && (
                              <p className="text-gray-500">Ref: {availabilityResult.coverageDetails.sourceReference}</p>
                            )}
                            <p className="text-gray-500">
                              Coverage: {new Date(availabilityResult.coverageDetails.coversFromUtc).toLocaleDateString()} to{' '}
                              {new Date(availabilityResult.coverageDetails.coversToUtc).toLocaleDateString()} (Verified by {availabilityResult.coverageDetails.verifiedBy})
                            </p>
                          </div>
                        )}

                        {/* Request Venue Submission Section (When Available) */}
                        {availabilityResult.status === 'Available' && (
                          <div className="border-t border-emerald-200 pt-3 space-y-3">
                            {/* Check if venue has assigned managers */}
                            {selectedVenue.managers?.length === 0 ? (
                              <div className="p-3 bg-amber-50 border border-amber-300 rounded-md text-xs text-amber-900 flex items-start gap-2">
                                <AlertTriangle className="h-4 w-4 text-amber-600 shrink-0 mt-0.5" />
                                <div>
                                  <p className="font-semibold">No venue manager configured</p>
                                  <p className="text-[11px] mt-0.5">
                                    This venue currently has no assigned facility managers to review requests. A booking request
                                    cannot be submitted until facility administration assigns a manager.
                                  </p>
                                </div>
                              </div>
                            ) : isEventCoordinator ? (
                              <div>
                                <button
                                  type="button"
                                  onClick={() => {
                                    setShowRequestModal(true);
                                    setSubmitError(null);
                                  }}
                                  className="w-full inline-flex justify-center items-center py-2.5 px-4 rounded-md shadow-sm text-xs font-semibold text-white bg-emerald-600 hover:bg-emerald-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-emerald-500"
                                >
                                  <Send className="h-4 w-4 mr-2" />
                                  Request This Venue
                                </button>
                              </div>
                            ) : (
                              <div className="p-2.5 bg-gray-50 border border-gray-200 rounded text-center text-xs text-gray-500">
                                Event Coordinator role is required to submit booking requests.
                              </div>
                            )}
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                )}
              </div>
            </div>
          )}

          {/* =========================================================================
              MODAL: BOOKING REQUEST SUBMISSION
              ========================================================================= */}
          {showRequestModal && selectedVenue && availabilityResult && (
            <div className="fixed inset-0 z-50 overflow-y-auto bg-gray-900/50 flex items-center justify-center p-4">
              <div className="bg-white rounded-xl shadow-xl max-w-lg w-full p-6 space-y-4">
                <div className="flex items-start justify-between border-b pb-3">
                  <div>
                    <h3 className="text-base font-bold text-gray-900 flex items-center gap-2">
                      <Send className="h-5 w-5 text-indigo-600" />
                      Submit Booking Request
                    </h3>
                    <p className="text-xs text-gray-500 mt-0.5">
                      Venue: <strong>{selectedVenue.name}</strong> ({selectedVenue.code})
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={() => setShowRequestModal(false)}
                    className="text-gray-400 hover:text-gray-600"
                  >
                    <X className="h-5 w-5" />
                  </button>
                </div>

                {submitError && (
                  <div className="p-3 bg-red-50 border border-red-200 rounded-md text-xs text-red-700 flex items-center gap-2">
                    <AlertTriangle className="h-4 w-4 text-red-500 shrink-0" />
                    {submitError}
                  </div>
                )}

                <form onSubmit={handleSubmitBookingRequest} className="space-y-4 text-xs">
                  <div>
                    <label className="block font-medium text-gray-700 mb-1">Event Title *</label>
                    <input
                      type="text"
                      placeholder="e.g. Annual Student Robotics Exhibition"
                      value={eventTitle}
                      onChange={(e) => setEventTitle(e.target.value)}
                      className="w-full rounded-md border-gray-300 shadow-sm text-xs"
                      required
                    />
                  </div>

                  <div>
                    <label className="block font-medium text-gray-700 mb-1">Description (Optional)</label>
                    <textarea
                      rows={2}
                      placeholder="Purpose, agenda, VIP guests, or audio-visual requirements..."
                      value={eventDescription}
                      onChange={(e) => setEventDescription(e.target.value)}
                      className="w-full rounded-md border-gray-300 shadow-sm text-xs"
                    />
                  </div>

                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block font-medium text-gray-700 mb-1">
                        Expected Attendance (Max: {selectedVenue.capacity}) *
                      </label>
                      <input
                        type="number"
                        min="1"
                        max={selectedVenue.capacity}
                        value={expectedAttendance}
                        onChange={(e) => setExpectedAttendance(parseInt(e.target.value, 10) || 1)}
                        className="w-full rounded-md border-gray-300 shadow-sm text-xs"
                        required
                      />
                    </div>
                    <div>
                      <label className="block font-medium text-gray-700 mb-1">Occupied Buffers</label>
                      <div className="p-2 bg-gray-50 rounded border text-gray-600">
                        +{availabilityResult.setupMinutes}m setup / +{availabilityResult.cleanupMinutes}m cleanup
                      </div>
                    </div>
                  </div>

                  {/* General Venue Rules Display & Acknowledgment */}
                  <div className="p-3 bg-indigo-50/50 rounded-lg border border-indigo-100 space-y-2">
                    <span className="font-semibold text-indigo-950 flex items-center gap-1.5">
                      <FileText className="h-4 w-4 text-indigo-600" />
                      General Venue Rules & University Policies
                    </span>
                    <div className="p-2 bg-white rounded border border-gray-200 text-gray-700 text-[11px] max-h-32 overflow-y-auto whitespace-pre-line">
                      {selectedVenue.generalRules || 'Standard university venue policies apply: clean venue after use, report any property damages, and adhere to sound regulations.'}
                    </div>
                    <label className="flex items-start gap-2 cursor-pointer pt-1">
                      <input
                        type="checkbox"
                        checked={rulesAcknowledged}
                        onChange={(e) => setRulesAcknowledged(e.target.checked)}
                        className="mt-0.5 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                        required
                      />
                      <span className="text-gray-700 font-medium">
                        I acknowledge and agree to comply with all general venue-use rules and campus protocols.
                      </span>
                    </label>
                  </div>

                  <div className="flex justify-end gap-2 pt-2 border-t">
                    <button
                      type="button"
                      onClick={() => setShowRequestModal(false)}
                      className="px-4 py-2 border border-gray-300 rounded-md text-gray-700 hover:bg-gray-50"
                    >
                      Cancel
                    </button>
                    <button
                      type="submit"
                      disabled={submittingRequest || !rulesAcknowledged}
                      className="px-4 py-2 bg-indigo-600 text-white rounded-md font-semibold hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-1.5"
                    >
                      {submittingRequest ? (
                        <>
                          <RefreshCw className="h-4 w-4 animate-spin" />
                          Submitting...
                        </>
                      ) : (
                        <>
                          <Send className="h-4 w-4" />
                          Submit Request
                        </>
                      )}
                    </button>
                  </div>
                </form>
              </div>
            </div>
          )}

          {/* =========================================================================
              TAB 2: MY BOOKING REQUESTS (Event Coordinators)
              ========================================================================= */}
          {activeTab === 'my-requests' && (
            <div className="space-y-6">
              <div className="flex items-center justify-between border-b pb-3">
                <div>
                  <h2 className="text-base font-bold text-gray-900 flex items-center gap-2">
                    <ClipboardList className="h-5 w-5 text-indigo-600" />
                    My Submitted Booking Requests
                  </h2>
                  <p className="text-xs text-gray-500 mt-0.5">
                    Track reviewer status, evaluate proposed conditions, and view notifications for your reservations.
                  </p>
                </div>
                <button
                  type="button"
                  onClick={refreshMyRequests}
                  className="px-3 py-1.5 border border-gray-300 rounded text-xs text-gray-600 hover:bg-gray-50 flex items-center gap-1.5"
                >
                  <RefreshCw className={`h-3.5 w-3.5 ${loadingMyRequests ? 'animate-spin' : ''}`} />
                  Refresh
                </button>
              </div>

              {actionSuccess && (
                <div className="p-3 bg-emerald-50 border border-emerald-200 rounded text-xs text-emerald-800 flex items-center gap-2">
                  <CheckCircle2 className="h-4 w-4 text-emerald-600 shrink-0" />
                  {actionSuccess}
                </div>
              )}

              {myRequestsError && (
                <div className="p-3 bg-red-50 border border-red-200 rounded text-xs text-red-700 flex items-center gap-2">
                  <AlertTriangle className="h-4 w-4 text-red-500 shrink-0" />
                  {myRequestsError}
                </div>
              )}

              {loadingMyRequests ? (
                <div className="flex items-center justify-center p-8 text-xs text-gray-500 gap-2">
                  <RefreshCw className="h-4 w-4 animate-spin text-indigo-600" />
                  Loading your requests...
                </div>
              ) : myRequests.length === 0 ? (
                <div className="text-center p-12 bg-white rounded-lg border border-gray-200 text-xs text-gray-500 space-y-2">
                  <ClipboardList className="h-8 w-8 text-gray-300 mx-auto" />
                  <p className="font-semibold text-gray-700">No booking requests submitted yet.</p>
                  <p>Check venue availability in the catalog and submit a request to get started.</p>
                </div>
              ) : (
                <div className="space-y-4">
                  {myRequests.map((req) => (
                    <div key={req.id} className="bg-white rounded-lg border border-gray-200 shadow-sm p-5 space-y-3 text-xs">
                      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b pb-3">
                        <div>
                          <div className="flex items-center gap-2">
                            <h3 className="text-sm font-bold text-gray-900">{req.eventTitle}</h3>
                            <span
                              className={`text-[10px] font-bold px-2 py-0.5 rounded-full uppercase tracking-wider ${
                                req.status === 'PendingReview'
                                  ? 'bg-amber-100 text-amber-800'
                                  : req.status === 'ApprovedWithConditions'
                                  ? 'bg-purple-100 text-purple-800'
                                  : req.status === 'AwaitingConfirmation'
                                  ? 'bg-emerald-100 text-emerald-800'
                                  : req.status === 'Rejected'
                                  ? 'bg-red-100 text-red-800'
                                  : 'bg-gray-100 text-gray-700'
                              }`}
                            >
                              {req.status === 'PendingReview' && 'Pending Manager Review'}
                              {req.status === 'ApprovedWithConditions' && 'Conditions Pending Acceptance'}
                              {req.status === 'AwaitingConfirmation' && 'Awaiting Booking Confirmation'}
                              {req.status === 'Rejected' && 'Rejected'}
                              {req.status === 'Cancelled' && 'Cancelled'}
                            </span>
                          </div>
                          <p className="text-gray-500 text-[11px] mt-0.5">
                            Venue: <strong>{req.venueName}</strong> ({req.venueCode}) &bull; Submitted {new Date(req.createdAtUtc).toLocaleDateString()}
                          </p>
                        </div>
                        <div className="text-right text-[11px] text-gray-500">
                          <span>Attendance: <strong>{req.expectedAttendance}</strong></span>
                        </div>
                      </div>

                      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-gray-600">
                        <div>
                          <span className="text-gray-400 block text-[10px]">Event Interval</span>
                          <span className="font-medium">
                            {new Date(req.eventStartUtc).toLocaleDateString()} &bull;{' '}
                            {new Date(req.eventStartUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} -{' '}
                            {new Date(req.eventEndUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} UTC
                          </span>
                        </div>
                        <div>
                          <span className="text-gray-400 block text-[10px]">Setup / Cleanup Buffers</span>
                          <span className="font-medium">+{req.setupMinutes}m setup / +{req.cleanupMinutes}m cleanup</span>
                        </div>
                      </div>

                      {req.description && (
                        <p className="text-gray-600 bg-gray-50 p-2.5 rounded border border-gray-100">
                          {req.description}
                        </p>
                      )}

                      {/* CONDITIONAL APPROVAL ACCEPTANCE BOX */}
                      {req.status === 'ApprovedWithConditions' && (
                        <div className="p-4 bg-purple-50/70 border border-purple-200 rounded-lg space-y-3">
                          <div className="flex items-center gap-2 text-purple-900 font-bold">
                            <AlertCircle className="h-4 w-4 text-purple-600" />
                            Approval Conditions Imposed by Venue Custodian:
                          </div>
                          <blockquote className="bg-white p-3 rounded border border-purple-200 text-purple-950 font-medium whitespace-pre-line">
                            {req.conditions}
                          </blockquote>
                          {req.reviewNotes && (
                            <p className="text-[11px] text-gray-600 italic">
                              Reviewer Notes: {req.reviewNotes}
                            </p>
                          )}
                          <div className="flex items-center gap-3 pt-2">
                            <button
                              type="button"
                              onClick={() => handleRespondToConditions(req.id, true)}
                              className="px-4 py-1.5 bg-emerald-600 hover:bg-emerald-700 text-white rounded font-semibold flex items-center gap-1.5 shadow-sm"
                            >
                              <Check className="h-3.5 w-3.5" />
                              Accept Conditions
                            </button>
                            <button
                              type="button"
                              onClick={() => handleRespondToConditions(req.id, false)}
                              className="px-4 py-1.5 border border-red-300 text-red-700 hover:bg-red-50 rounded font-medium flex items-center gap-1.5"
                            >
                              <X className="h-3.5 w-3.5" />
                              Decline & Cancel
                            </button>
                          </div>
                        </div>
                      )}

                      {/* AWAITING CONFIRMATION BANNER */}
                      {req.status === 'AwaitingConfirmation' && (
                        <div className="p-3 bg-emerald-50 border border-emerald-200 rounded text-emerald-800 flex items-center gap-2">
                          <CheckCircle2 className="h-4 w-4 text-emerald-600 shrink-0" />
                          <span>
                            Request approved by venue custodian. Awaiting final double-booking verification and reservation confirmation in Phase 1C.3.
                          </span>
                        </div>
                      )}

                      {/* REJECTION REASON */}
                      {req.status === 'Rejected' && (
                        <div className="p-3 bg-red-50 border border-red-200 rounded text-red-800 space-y-1">
                          <span className="font-semibold flex items-center gap-1 text-red-700">
                            <X className="h-4 w-4" />
                            Request Rejected
                          </span>
                          <p className="text-gray-700">
                            <strong>Reason:</strong> {req.reviewNotes || 'No specific explanation provided.'}
                          </p>
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}

          {/* =========================================================================
              TAB 3: MANAGER REVIEW QUEUE (FacilityAdmin & UniversityAdmin)
              ========================================================================= */}
          {activeTab === 'review-queue' && isFacilityStaff && (
            <div className="space-y-6">
              <div className="flex items-center justify-between border-b pb-3">
                <div>
                  <h2 className="text-base font-bold text-gray-900 flex items-center gap-2">
                    <CalendarCheck className="h-5 w-5 text-indigo-600" />
                    Manager Review Queue
                  </h2>
                  <p className="text-xs text-gray-500 mt-0.5">
                    Evaluate pending booking requests, propose approval conditions, or reject unviable reservations.
                  </p>
                </div>
                <button
                  type="button"
                  onClick={refreshReviewQueue}
                  className="px-3 py-1.5 border border-gray-300 rounded text-xs text-gray-600 hover:bg-gray-50 flex items-center gap-1.5"
                >
                  <RefreshCw className={`h-3.5 w-3.5 ${loadingReviewQueue ? 'animate-spin' : ''}`} />
                  Refresh
                </button>
              </div>

              {reviewQueueError && (
                <div className="p-3 bg-red-50 border border-red-200 rounded text-xs text-red-700 flex items-center gap-2">
                  <AlertTriangle className="h-4 w-4 text-red-500 shrink-0" />
                  {reviewQueueError}
                </div>
              )}

              {loadingReviewQueue ? (
                <div className="flex items-center justify-center p-8 text-xs text-gray-500 gap-2">
                  <RefreshCw className="h-4 w-4 animate-spin text-indigo-600" />
                  Loading review queue...
                </div>
              ) : reviewQueue.length === 0 ? (
                <div className="text-center p-12 bg-white rounded-lg border border-gray-200 text-xs text-gray-500 space-y-2">
                  <CheckCircle2 className="h-8 w-8 text-emerald-400 mx-auto" />
                  <p className="font-semibold text-gray-700">All caught up! No requests pending review.</p>
                  <p>When coordinators submit requests for your managed venues, they will appear here.</p>
                </div>
              ) : (
                <div className="space-y-4">
                  {reviewQueue.map((req) => {
                    const isSelfSubmission = user?.id === req.requesterId;
                    return (
                      <div key={req.id} className="bg-white rounded-lg border border-gray-200 shadow-sm p-5 space-y-3 text-xs">
                        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b pb-3">
                          <div>
                            <div className="flex items-center gap-2">
                              <h3 className="text-sm font-bold text-gray-900">{req.eventTitle}</h3>
                              <span
                                className={`text-[10px] font-bold px-2 py-0.5 rounded-full uppercase tracking-wider ${
                                  req.status === 'PendingReview'
                                    ? 'bg-amber-100 text-amber-800'
                                    : req.status === 'ApprovedWithConditions'
                                    ? 'bg-purple-100 text-purple-800'
                                    : 'bg-emerald-100 text-emerald-800'
                                }`}
                              >
                                {req.status}
                              </span>
                            </div>
                            <p className="text-gray-500 text-[11px] mt-0.5">
                              Venue: <strong>{req.venueName}</strong> ({req.venueCode}) &bull; Requester: <strong>{req.requesterEmail}</strong>
                            </p>
                          </div>

                          {/* Segregation of Duties Warning or Action Buttons */}
                          <div>
                            {isSelfSubmission ? (
                              <div className="inline-flex items-center gap-1.5 p-2 bg-amber-50 border border-amber-200 rounded text-amber-900 font-medium text-[11px]">
                                <AlertTriangle className="h-4 w-4 text-amber-600 shrink-0" />
                                <span>Self-Submission: Segregation of duties applies. Another manager must review.</span>
                              </div>
                            ) : req.status === 'PendingReview' ? (
                              <div className="flex items-center gap-2">
                                <button
                                  type="button"
                                  onClick={() => {
                                    setSelectedRequestForReview(req);
                                    setReviewDecision('Approve');
                                    setReviewNotes('');
                                    setReviewConditions('');
                                    setReviewActionError(null);
                                  }}
                                  className="px-3 py-1.5 bg-emerald-600 hover:bg-emerald-700 text-white rounded font-medium shadow-sm"
                                >
                                  Approve
                                </button>
                                <button
                                  type="button"
                                  onClick={() => {
                                    setSelectedRequestForReview(req);
                                    setReviewDecision('ApproveWithConditions');
                                    setReviewNotes('');
                                    setReviewConditions('');
                                    setReviewActionError(null);
                                  }}
                                  className="px-3 py-1.5 bg-purple-600 hover:bg-purple-700 text-white rounded font-medium shadow-sm"
                                >
                                  Conditions...
                                </button>
                                <button
                                  type="button"
                                  onClick={() => {
                                    setSelectedRequestForReview(req);
                                    setReviewDecision('Reject');
                                    setReviewNotes('');
                                    setReviewConditions('');
                                    setReviewActionError(null);
                                  }}
                                  className="px-3 py-1.5 border border-red-300 text-red-700 hover:bg-red-50 rounded font-medium"
                                >
                                  Reject...
                                </button>
                              </div>
                            ) : (
                              <span className="text-gray-500 italic">Review completed</span>
                            )}
                          </div>
                        </div>

                        <div className="grid grid-cols-1 sm:grid-cols-3 gap-2 text-gray-600">
                          <div>
                            <span className="text-gray-400 block text-[10px]">Interval</span>
                            <span className="font-medium">
                              {new Date(req.eventStartUtc).toLocaleDateString()} {new Date(req.eventStartUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} - {new Date(req.eventEndUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                            </span>
                          </div>
                          <div>
                            <span className="text-gray-400 block text-[10px]">Buffers</span>
                            <span className="font-medium">+{req.setupMinutes}m setup / +{req.cleanupMinutes}m cleanup</span>
                          </div>
                          <div>
                            <span className="text-gray-400 block text-[10px]">Attendance</span>
                            <span className="font-medium">{req.expectedAttendance} attendees</span>
                          </div>
                        </div>

                        {req.description && (
                          <p className="text-gray-600 bg-gray-50 p-2.5 rounded border border-gray-100">
                            {req.description}
                          </p>
                        )}
                      </div>
                    );
                  })}
                </div>
              )}

              {/* REVIEW DECISION MODAL */}
              {selectedRequestForReview && (
                <div className="fixed inset-0 z-50 overflow-y-auto bg-gray-900/50 flex items-center justify-center p-4">
                  <div className="bg-white rounded-xl shadow-xl max-w-lg w-full p-6 space-y-4 text-xs">
                    <div className="flex items-start justify-between border-b pb-3">
                      <div>
                        <h3 className="text-base font-bold text-gray-900">
                          Review Booking: {selectedRequestForReview.eventTitle}
                        </h3>
                        <p className="text-gray-500 mt-0.5">
                          Requested by {selectedRequestForReview.requesterEmail} for {selectedRequestForReview.venueName}
                        </p>
                      </div>
                      <button
                        type="button"
                        onClick={() => setSelectedRequestForReview(null)}
                        className="text-gray-400 hover:text-gray-600"
                      >
                        <X className="h-5 w-5" />
                      </button>
                    </div>

                    {reviewActionError && (
                      <div className="p-3 bg-red-50 border border-red-200 rounded text-red-700 flex items-center gap-2">
                        <AlertTriangle className="h-4 w-4 text-red-500 shrink-0" />
                        {reviewActionError}
                      </div>
                    )}

                    <form onSubmit={handleSubmitReview} className="space-y-4">
                      <div>
                        <label className="block font-semibold text-gray-700 mb-1">Decision</label>
                        <div className="grid grid-cols-3 gap-2">
                          <button
                            type="button"
                            onClick={() => setReviewDecision('Approve')}
                            className={`py-2 px-3 rounded font-medium border text-center transition-colors ${
                              reviewDecision === 'Approve'
                                ? 'bg-emerald-600 text-white border-emerald-600'
                                : 'bg-gray-50 text-gray-700 border-gray-200 hover:bg-gray-100'
                            }`}
                          >
                            Approve
                          </button>
                          <button
                            type="button"
                            onClick={() => setReviewDecision('ApproveWithConditions')}
                            className={`py-2 px-3 rounded font-medium border text-center transition-colors ${
                              reviewDecision === 'ApproveWithConditions'
                                ? 'bg-purple-600 text-white border-purple-600'
                                : 'bg-gray-50 text-gray-700 border-gray-200 hover:bg-gray-100'
                            }`}
                          >
                            With Conditions
                          </button>
                          <button
                            type="button"
                            onClick={() => setReviewDecision('Reject')}
                            className={`py-2 px-3 rounded font-medium border text-center transition-colors ${
                              reviewDecision === 'Reject'
                                ? 'bg-red-600 text-white border-red-600'
                                : 'bg-gray-50 text-gray-700 border-gray-200 hover:bg-gray-100'
                            }`}
                          >
                            Reject
                          </button>
                        </div>
                      </div>

                      {reviewDecision === 'ApproveWithConditions' && (
                        <div>
                          <label className="block font-semibold text-gray-700 mb-1">
                            Specific Approval Conditions * (Required)
                          </label>
                          <textarea
                            rows={3}
                            placeholder="e.g. 1. Sound system power cut after 19:00.\n2. Hire 2 campus security guards.\n3. Complete trash disposal by 21:00."
                            value={reviewConditions}
                            onChange={(e) => setReviewConditions(e.target.value)}
                            className="w-full rounded border-gray-300 text-xs shadow-sm"
                            required
                          />
                        </div>
                      )}

                      <div>
                        <label className="block font-semibold text-gray-700 mb-1">
                          {reviewDecision === 'Reject' ? 'Rejection Reason * (Required)' : 'Review Notes (Optional)'}
                        </label>
                        <textarea
                          rows={2}
                          placeholder={reviewDecision === 'Reject' ? 'Explain why this request is unviable...' : 'Additional notes for coordinator...'}
                          value={reviewNotes}
                          onChange={(e) => setReviewNotes(e.target.value)}
                          className="w-full rounded border-gray-300 text-xs shadow-sm"
                          required={reviewDecision === 'Reject'}
                        />
                      </div>

                      <div className="flex justify-end gap-2 pt-2 border-t">
                        <button
                          type="button"
                          onClick={() => setSelectedRequestForReview(null)}
                          className="px-4 py-2 border border-gray-300 rounded text-gray-700 hover:bg-gray-50"
                        >
                          Cancel
                        </button>
                        <button
                          type="submit"
                          disabled={submittingReview}
                          className="px-4 py-2 bg-indigo-600 text-white rounded font-semibold hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-1.5"
                        >
                          {submittingReview ? (
                            <>
                              <RefreshCw className="h-4 w-4 animate-spin" />
                              Saving Decision...
                            </>
                          ) : (
                            'Submit Decision'
                          )}
                        </button>
                      </div>
                    </form>
                  </div>
                </div>
              )}
            </div>
          )}

          {/* =========================================================================
              TAB 4: CALENDAR & VENUE RULES MANAGEMENT (Facility Staff & Admin)
              ========================================================================= */}
          {activeTab === 'management' && isFacilityStaff && (
            <div className="space-y-6">
              {/* Venue Selector Row */}
              <div className="bg-white p-4 rounded-lg border border-gray-200 shadow-sm flex flex-col sm:flex-row sm:items-center justify-between gap-4">
                <div className="flex items-center gap-3">
                  <label className="text-xs font-semibold text-gray-700">Managing Venue:</label>
                  <select
                    value={selectedVenueId}
                    onChange={(e) => {
                      setSelectedVenueId(e.target.value);
                      const current = venues.find((v) => v.id === e.target.value);
                      if (current) populateVenueRulesForm(current);
                    }}
                    className="text-xs rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
                  >
                    {venues.map((v) => (
                      <option key={v.id} value={v.id}>
                        {v.name} ({v.type})
                      </option>
                    ))}
                  </select>
                </div>

                <div className="flex items-center gap-2">
                  <div className="flex rounded-md border border-gray-200 p-0.5 bg-gray-50 text-xs">
                    <button
                      type="button"
                      onClick={() => setManagementSubView('calendar')}
                      className={`px-3 py-1 font-medium rounded ${
                        managementSubView === 'calendar'
                          ? 'bg-white text-indigo-700 shadow-sm'
                          : 'text-gray-600 hover:text-gray-900'
                      }`}
                    >
                      Calendar & Blocks
                    </button>
                    <button
                      type="button"
                      onClick={() => setManagementSubView('rules')}
                      className={`px-3 py-1 font-medium rounded ${
                        managementSubView === 'rules'
                          ? 'bg-white text-indigo-700 shadow-sm'
                          : 'text-gray-600 hover:text-gray-900'
                      }`}
                    >
                      Venue Rules & Policies
                    </button>
                  </div>

                  <div>
                    {isAssignedManager ? (
                      <span className="inline-flex items-center gap-1 text-xs font-semibold px-2.5 py-1 rounded-full bg-emerald-100 text-emerald-800">
                        <ShieldCheck className="h-3.5 w-3.5" />
                        Authorized Facility Staff / Admin
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1 text-xs font-semibold px-2.5 py-1 rounded-full bg-amber-100 text-amber-800">
                        <AlertTriangle className="h-3.5 w-3.5" />
                        Not Assigned to Manage this Venue (Read-Only)
                      </span>
                    )}
                  </div>
                </div>
              </div>

              {managementSuccess && (
                <div className="p-3 bg-emerald-50 border border-emerald-200 rounded-md text-xs text-emerald-800 flex items-center gap-2">
                  <CheckCircle2 className="h-4 w-4 text-emerald-600" />
                  {managementSuccess}
                </div>
              )}

              {managementError && (
                <div className="p-3 bg-red-50 border border-red-200 rounded-md text-xs text-red-800 flex items-center gap-2">
                  <AlertTriangle className="h-4 w-4 text-red-600" />
                  {managementError}
                </div>
              )}

              {/* VENUE MANAGERS SECTION (UniversityAdmin with completed MFA) */}
              {canManageVenueManagers && selectedVenue && (
                <div className="bg-white p-5 rounded-lg border border-gray-200 shadow-sm space-y-4">
                  <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 border-b pb-3 border-gray-100">
                    <div>
                      <h3 className="text-sm font-bold text-gray-900 flex items-center gap-2">
                        <Users className="h-4 w-4 text-indigo-600" />
                        Venue Managers
                      </h3>
                      <p className="text-xs text-gray-500 mt-0.5">
                        Authorized facility staff assigned to manage calendar blocks, verified coverages, and review booking requests for {selectedVenue.name}.
                      </p>
                    </div>
                    <button
                      type="button"
                      onClick={handleOpenAssignModal}
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-md bg-indigo-600 text-white hover:bg-indigo-700 shadow-sm transition-colors self-start sm:self-auto"
                    >
                      <UserPlus className="h-3.5 w-3.5" />
                      Assign Venue Manager
                    </button>
                  </div>

                  {/* Manager List */}
                  {selectedVenue.managers?.length === 0 ? (
                    <div className="p-3 bg-amber-50 border border-amber-200 rounded-md text-xs text-amber-800 flex items-center gap-2">
                      <AlertTriangle className="h-4 w-4 text-amber-600 shrink-0" />
                      <div>
                        <span className="font-semibold">No managers currently assigned to this venue.</span>
                        <p className="text-amber-700 mt-0.5">
                          Booking requests cannot be submitted for this venue until an authorized FacilityAdmin is assigned.
                        </p>
                      </div>
                    </div>
                  ) : (
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                      {selectedVenue.managers.map((mgr) => (
                        <div
                          key={mgr.userId}
                          className="p-3 bg-gray-50 border border-gray-200 rounded-md text-xs flex items-center justify-between"
                        >
                          <div>
                            <div className="font-semibold text-gray-900">{mgr.email}</div>
                            {mgr.displayName && <div className="text-[11px] text-gray-500">{mgr.displayName}</div>}
                            <div className="text-[10px] text-gray-400 mt-0.5">
                              Assigned: {new Date(mgr.assignedAtUtc).toLocaleDateString()}
                            </div>
                          </div>
                          <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-blue-100 text-blue-800">
                            Facility Admin
                          </span>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              )}

              {/* SUBVIEW 1: CALENDAR COVERAGES & BLOCKS */}
              {managementSubView === 'calendar' && (
                <>
                  {loadingManagement ? (
                    <div className="flex items-center justify-center p-8 text-xs text-gray-500 gap-2">
                      <RefreshCw className="h-4 w-4 animate-spin text-indigo-600" />
                      Loading calendar data...
                    </div>
                  ) : (
                    <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                      {/* Verified Coverages Column */}
                      <div className="bg-white p-5 rounded-lg border border-gray-200 shadow-sm space-y-4">
                        <div className="flex items-center justify-between border-b pb-2">
                          <h3 className="text-sm font-bold text-gray-900 flex items-center gap-1.5">
                            <ShieldCheck className="h-4 w-4 text-emerald-600" />
                            Verified Calendar Coverage
                          </h3>
                          <span className="text-[11px] text-gray-400">{coverages.length} records</span>
                        </div>

                        <p className="text-xs text-gray-500">
                          Dates within verified coverage periods permit booking availability inquiries. Dates outside return{' '}
                          <em>Availability unverified</em>.
                        </p>

                        {/* Coverage List */}
                        <div className="space-y-2 max-h-60 overflow-y-auto">
                          {coverages.length === 0 ? (
                            <p className="text-xs text-gray-400 italic">No verified coverage periods recorded.</p>
                          ) : (
                            coverages.map((cov) => (
                              <div key={cov.id} className="p-3 bg-gray-50 rounded border text-xs space-y-1">
                                <div className="flex items-center justify-between">
                                  <span className="font-semibold text-gray-800">{cov.sourceName}</span>
                                  <span className="text-[10px] text-gray-400">
                                    Verified {new Date(cov.verifiedAtUtc).toLocaleDateString()}
                                  </span>
                                </div>
                                <p className="text-gray-600 text-[11px]">
                                  Range: {new Date(cov.coversFromUtc).toLocaleDateString()} to{' '}
                                  {new Date(cov.coversToUtc).toLocaleDateString()}
                                </p>
                                {cov.sourceReference && (
                                  <p className="text-gray-500 text-[10px]">Ref: {cov.sourceReference}</p>
                                )}
                                <p className="text-gray-400 text-[10px]">By: {cov.verifiedByUserEmail}</p>
                              </div>
                            ))
                          )}
                        </div>

                        {/* Add Coverage Form (if manager) */}
                        {isAssignedManager && (
                          <form onSubmit={handleAddCoverage} className="border-t pt-3 space-y-2 text-xs">
                            <h4 className="font-semibold text-gray-800 text-xs flex items-center gap-1">
                              <Plus className="h-3.5 w-3.5 text-indigo-600" />
                              Record New Verification Coverage
                            </h4>
                            <div>
                              <input
                                type="text"
                                placeholder="Source (e.g. book.uom.lk, uomba.net)"
                                value={coverageSource}
                                onChange={(e) => setCoverageSource(e.target.value)}
                                className="w-full text-xs rounded border-gray-300 shadow-sm"
                                required
                              />
                            </div>
                            <div>
                              <input
                                type="text"
                                placeholder="Reference (e.g. Semester 2 Term Timetable)"
                                value={coverageReference}
                                onChange={(e) => setCoverageReference(e.target.value)}
                                className="w-full text-xs rounded border-gray-300 shadow-sm"
                              />
                            </div>
                            <div className="grid grid-cols-2 gap-2">
                              <input
                                type="date"
                                value={coverageFromDate}
                                onChange={(e) => setCoverageFromDate(e.target.value)}
                                className="text-xs rounded border-gray-300 shadow-sm"
                                required
                              />
                              <input
                                type="date"
                                value={coverageToDate}
                                onChange={(e) => setCoverageToDate(e.target.value)}
                                className="text-xs rounded border-gray-300 shadow-sm"
                                required
                              />
                            </div>
                            <div>
                              <input
                                type="text"
                                placeholder="Optional notes or audit comments"
                                value={coverageNotes}
                                onChange={(e) => setCoverageNotes(e.target.value)}
                                className="w-full text-xs rounded border-gray-300 shadow-sm"
                              />
                            </div>
                            <button
                              type="submit"
                              className="w-full py-1.5 px-3 bg-emerald-600 text-white rounded text-xs font-medium hover:bg-emerald-700"
                            >
                              Record Coverage Period
                            </button>
                          </form>
                        )}
                      </div>

                      {/* Calendar Blocks Column */}
                      <div className="bg-white p-5 rounded-lg border border-gray-200 shadow-sm space-y-4">
                        <div className="flex items-center justify-between border-b pb-2">
                          <h3 className="text-sm font-bold text-gray-900 flex items-center gap-1.5">
                            <Calendar className="h-4 w-4 text-indigo-600" />
                            Calendar Blocks (Classes, Exams, Maintenance)
                          </h3>
                          <span className="text-[11px] text-gray-400">{blocks.length} blocks</span>
                        </div>

                        <p className="text-xs text-gray-500">
                          Blocked times preventing event reservations. Sourced from academic timetables, exams, or external bookings.
                        </p>

                        {/* Blocks List */}
                        <div className="space-y-2 max-h-60 overflow-y-auto">
                          {blocks.length === 0 ? (
                            <p className="text-xs text-gray-400 italic">No calendar blocks recorded for this venue.</p>
                          ) : (
                            blocks.map((block) => (
                              <div
                                key={block.id}
                                className="p-3 bg-gray-50 rounded border text-xs flex items-center justify-between"
                              >
                                <div>
                                  <div className="flex items-center gap-2">
                                    <span className="font-semibold text-gray-800">{block.title}</span>
                                    <span className="text-[10px] bg-indigo-100 text-indigo-700 px-1.5 py-0.2 rounded">
                                      {block.blockType}
                                    </span>
                                  </div>
                                  <p className="text-gray-500 text-[11px]">
                                    {new Date(block.startUtc).toLocaleDateString()}{' '}
                                    {new Date(block.startUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} -{' '}
                                    {new Date(block.endUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                                  </p>
                                  <span className="text-[10px] text-gray-400 block">By: {block.createdByUserEmail}</span>
                                </div>

                                {isAssignedManager && (
                                  <button
                                    type="button"
                                    onClick={() => handleDeleteBlock(block.id, block.title)}
                                    className="text-gray-400 hover:text-red-600 p-1"
                                    title="Delete block"
                                  >
                                    <Trash2 className="h-4 w-4" />
                                  </button>
                                )}
                              </div>
                            ))
                          )}
                        </div>

                        {/* Add Block Form (if manager) */}
                        {isAssignedManager && (
                          <form onSubmit={handleAddBlock} className="border-t pt-3 space-y-2 text-xs">
                            <h4 className="font-semibold text-gray-800 text-xs flex items-center gap-1">
                              <Plus className="h-3.5 w-3.5 text-indigo-600" />
                              Add Calendar Block
                            </h4>
                            <div className="grid grid-cols-3 gap-2">
                              <input
                                type="text"
                                placeholder="Block Title (e.g. CS2012 Lecture)"
                                value={blockTitle}
                                onChange={(e) => setBlockTitle(e.target.value)}
                                className="col-span-2 text-xs rounded border-gray-300 shadow-sm"
                                required
                              />
                              <select
                                value={blockType}
                                onChange={(e) => setBlockType(e.target.value)}
                                className="text-xs rounded border-gray-300 shadow-sm"
                              >
                                <option value="Class">Class</option>
                                <option value="Exam">Exam</option>
                                <option value="Maintenance">Maintenance</option>
                                <option value="ExternalBooking">External Booking</option>
                                <option value="Other">Other</option>
                              </select>
                            </div>
                            <div className="grid grid-cols-3 gap-2">
                              <input
                                type="date"
                                value={blockStartDate}
                                onChange={(e) => setBlockStartDate(e.target.value)}
                                className="text-xs rounded border-gray-300 shadow-sm"
                                required
                              />
                              <input
                                type="time"
                                value={blockStartTime}
                                onChange={(e) => setBlockStartTime(e.target.value)}
                                className="text-xs rounded border-gray-300 shadow-sm"
                                required
                              />
                              <input
                                type="time"
                                value={blockEndTime}
                                onChange={(e) => setBlockEndTime(e.target.value)}
                                className="text-xs rounded border-gray-300 shadow-sm"
                                required
                              />
                            </div>
                            <button
                              type="submit"
                              className="w-full py-1.5 px-3 bg-indigo-600 text-white rounded text-xs font-medium hover:bg-indigo-700"
                            >
                              Add Calendar Block
                            </button>
                          </form>
                        )}
                      </div>
                    </div>
                  )}
                </>
              )}

              {/* SUBVIEW 2: VENUE OPERATING RULES & GENERAL POLICIES */}
              {managementSubView === 'rules' && selectedVenue && (
                <div className="bg-white p-6 rounded-lg border border-gray-200 shadow-sm space-y-6">
                  <div className="border-b pb-3">
                    <h3 className="text-sm font-bold text-gray-900 flex items-center gap-2">
                      <Settings className="h-4 w-4 text-indigo-600" />
                      Manage Venue Rules & Operating Parameters for {selectedVenue.name}
                    </h3>
                    <p className="text-xs text-gray-500 mt-1">
                      Configure capacity limits, operating hours, setup buffers, and general venue policies shown to coordinators.
                    </p>
                  </div>

                  <form onSubmit={handleSaveVenueRules} className="space-y-4 text-xs">
                    <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-4 gap-4">
                      <div>
                        <label className="block font-medium text-gray-700 mb-1">Operating Hours Start</label>
                        <input
                          type="time"
                          value={ruleOpStart}
                          onChange={(e) => setRuleOpStart(e.target.value)}
                          disabled={!isAssignedManager}
                          className="w-full rounded border-gray-300 shadow-sm text-xs disabled:bg-gray-100"
                          required
                        />
                      </div>
                      <div>
                        <label className="block font-medium text-gray-700 mb-1">Operating Hours End</label>
                        <input
                          type="time"
                          value={ruleOpEnd}
                          onChange={(e) => setRuleOpEnd(e.target.value)}
                          disabled={!isAssignedManager}
                          className="w-full rounded border-gray-300 shadow-sm text-xs disabled:bg-gray-100"
                          required
                        />
                      </div>
                      <div>
                        <label className="block font-medium text-gray-700 mb-1">Maximum Capacity</label>
                        <input
                          type="number"
                          min="1"
                          value={ruleCapacity}
                          onChange={(e) => setRuleCapacity(parseInt(e.target.value, 10) || 1)}
                          disabled={!isAssignedManager}
                          className="w-full rounded border-gray-300 shadow-sm text-xs disabled:bg-gray-100"
                          required
                        />
                      </div>
                      <div>
                        <label className="block font-medium text-gray-700 mb-1">Min Notice (Hours)</label>
                        <input
                          type="number"
                          min="0"
                          value={ruleMinNotice}
                          onChange={(e) => setRuleMinNotice(parseInt(e.target.value, 10) || 0)}
                          disabled={!isAssignedManager}
                          className="w-full rounded border-gray-300 shadow-sm text-xs disabled:bg-gray-100"
                          required
                        />
                      </div>
                    </div>

                    <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                      <div>
                        <label className="block font-medium text-gray-700 mb-1">Default Setup (Minutes)</label>
                        <input
                          type="number"
                          min="0"
                          value={ruleSetupMinutes}
                          onChange={(e) => setRuleSetupMinutes(parseInt(e.target.value, 10) || 0)}
                          disabled={!isAssignedManager}
                          className="w-full rounded border-gray-300 shadow-sm text-xs disabled:bg-gray-100"
                          required
                        />
                      </div>
                      <div>
                        <label className="block font-medium text-gray-700 mb-1">Default Cleanup (Minutes)</label>
                        <input
                          type="number"
                          min="0"
                          value={ruleCleanupMinutes}
                          onChange={(e) => setRuleCleanupMinutes(parseInt(e.target.value, 10) || 0)}
                          disabled={!isAssignedManager}
                          className="w-full rounded border-gray-300 shadow-sm text-xs disabled:bg-gray-100"
                          required
                        />
                      </div>
                      <div>
                        <label className="block font-medium text-gray-700 mb-1">Max Duration (Hours)</label>
                        <input
                          type="number"
                          min="1"
                          max="24"
                          value={ruleMaxBookingHours}
                          onChange={(e) => setRuleMaxBookingHours(parseInt(e.target.value, 10) || 1)}
                          disabled={!isAssignedManager}
                          className="w-full rounded border-gray-300 shadow-sm text-xs disabled:bg-gray-100"
                          required
                        />
                      </div>
                    </div>

                    <div>
                      <label className="block font-medium text-gray-700 mb-1">General Venue-Use Rules & Policies</label>
                      <textarea
                        rows={5}
                        placeholder="Detail rules regarding sound systems, food/drinks, trash disposal, damage deposits, and security staff..."
                        value={ruleGeneralRules}
                        onChange={(e) => setRuleGeneralRules(e.target.value)}
                        disabled={!isAssignedManager}
                        className="w-full rounded border-gray-300 shadow-sm text-xs font-mono disabled:bg-gray-100"
                      />
                      <span className="text-[11px] text-gray-400">
                        These rules will be displayed to event coordinators during availability checking and must be acknowledged before submitting booking requests.
                      </span>
                    </div>

                    <div className="flex items-center gap-2 pt-1">
                      <input
                        type="checkbox"
                        id="approvalCheckbox"
                        checked={ruleRequiresApproval}
                        onChange={(e) => setRuleRequiresApproval(e.target.checked)}
                        disabled={!isAssignedManager}
                        className="rounded border-gray-300 text-indigo-600 focus:ring-indigo-500 disabled:opacity-50"
                      />
                      <label htmlFor="approvalCheckbox" className="text-gray-700 font-medium">
                        Student events require Student Welfare division approval
                      </label>
                    </div>

                    {isAssignedManager && (
                      <div className="flex justify-end pt-3 border-t">
                        <button
                          type="submit"
                          disabled={savingRules}
                          className="px-4 py-2 bg-indigo-600 text-white rounded font-semibold hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-1.5"
                        >
                          {savingRules ? (
                            <>
                              <RefreshCw className="h-4 w-4 animate-spin" />
                              Saving Rules...
                            </>
                          ) : (
                            'Save Venue Rules'
                          )}
                        </button>
                      </div>
                    )}
                  </form>
                </div>
              )}
            </div>
          )}
        </>
      )}

      {/* ASSIGN VENUE MANAGER MODAL (UniversityAdmin with MFA) */}
      {showAssignModal && selectedVenue && (
        <div className="fixed inset-0 z-50 overflow-y-auto bg-gray-900/50 flex items-center justify-center p-4">
          <div className="bg-white rounded-lg max-w-lg w-full p-6 space-y-4 shadow-xl border border-gray-200">
            <div className="flex items-center justify-between border-b pb-3 border-gray-100">
              <div className="flex items-center gap-2">
                <UserPlus className="h-5 w-5 text-indigo-600" />
                <h3 className="text-base font-bold text-gray-900">Assign Venue Manager</h3>
              </div>
              <button
                type="button"
                onClick={handleCloseAssignModal}
                className="text-gray-400 hover:text-gray-600"
                aria-label="Close"
              >
                <X className="h-5 w-5" />
              </button>
            </div>

            {/* Selected Venue Display */}
            <div className="bg-gray-50 border border-gray-200 rounded-md p-3 text-xs">
              <span className="text-gray-500 font-medium">Target Venue:</span>{' '}
              <strong className="text-gray-900">{selectedVenue.name}</strong>{' '}
              <span className="font-mono text-gray-500">({selectedVenue.code})</span>
            </div>

            {/* Search Users by Email */}
            <form onSubmit={handleSearchUsers} className="space-y-2">
              <label htmlFor="user-search-input" className="block text-xs font-semibold text-gray-700">
                Search Users Directory by Email
              </label>
              <div className="flex gap-2">
                <input
                  id="user-search-input"
                  type="text"
                  value={managerSearchEmail}
                  onChange={(e) => setManagerSearchEmail(e.target.value)}
                  placeholder="Search user by email (e.g. facility@campus.internal)..."
                  className="flex-1 text-xs rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
                />
                <button
                  type="submit"
                  disabled={loadingUserSearch}
                  className="px-3 py-1.5 text-xs font-semibold rounded-md bg-gray-100 hover:bg-gray-200 text-gray-700 border border-gray-300 flex items-center gap-1"
                >
                  {loadingUserSearch ? (
                    <RefreshCw className="h-3.5 w-3.5 animate-spin" />
                  ) : (
                    <Search className="h-3.5 w-3.5" />
                  )}
                  Search
                </button>
              </div>
            </form>

            {/* Search Results List */}
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <label className="block text-[11px] font-semibold text-gray-500 uppercase tracking-wide">
                  Accounts ({searchedUsers.length})
                </label>
                <span className="text-[10px] text-gray-400">Only accounts with FacilityAdmin role are eligible</span>
              </div>
              <div className="max-h-48 overflow-y-auto space-y-1.5 border border-gray-200 rounded-md p-2 bg-gray-50/50">
                {loadingUserSearch ? (
                  <div className="text-center py-4 text-xs text-gray-500 flex items-center justify-center gap-1.5">
                    <RefreshCw className="h-4 w-4 animate-spin text-indigo-600" />
                    Loading accounts...
                  </div>
                ) : searchedUsers.length === 0 ? (
                  <div className="text-center py-4 text-xs text-gray-500 italic">
                    No matching accounts found.
                  </div>
                ) : (
                  searchedUsers.map((u) => {
                    const hasFacilityAdmin = u.roles.includes('FacilityAdmin');
                    const isAlreadyManager = selectedVenue.managers?.some((m) => m.userId === u.id);
                    const isSelected = selectedUserForManager?.id === u.id;

                    return (
                      <div
                        key={u.id}
                        className={`p-2.5 rounded border text-xs flex items-center justify-between transition-colors ${
                          isSelected
                            ? 'border-indigo-600 bg-indigo-50/80 shadow-xs'
                            : 'border-gray-200 bg-white hover:border-gray-300'
                        }`}
                      >
                        <div>
                          <div className="font-semibold text-gray-900">{u.email}</div>
                          <div className="flex items-center gap-1.5 mt-0.5">
                            {u.roles.map((r) => (
                              <span
                                key={r}
                                className={`text-[10px] px-1.5 py-0.2 rounded font-medium ${
                                  r === 'FacilityAdmin'
                                    ? 'bg-blue-100 text-blue-800'
                                    : r === 'UniversityAdmin'
                                    ? 'bg-purple-100 text-purple-800'
                                    : 'bg-gray-100 text-gray-600'
                                }`}
                              >
                                {r}
                              </span>
                            ))}
                          </div>
                        </div>

                        <div>
                          {isAlreadyManager ? (
                            <span className="text-[11px] font-medium text-gray-400 italic">
                              Already Assigned
                            </span>
                          ) : hasFacilityAdmin ? (
                            <button
                              type="button"
                              onClick={() => setSelectedUserForManager(u)}
                              className={`px-2.5 py-1 text-xs font-semibold rounded ${
                                isSelected
                                  ? 'bg-indigo-600 text-white'
                                  : 'bg-indigo-50 text-indigo-700 hover:bg-indigo-100'
                              }`}
                            >
                              {isSelected ? 'Selected ✓' : 'Select'}
                            </button>
                          ) : (
                            <span className="text-[10px] text-gray-400 italic">
                              Requires FacilityAdmin
                            </span>
                          )}
                        </div>
                      </div>
                    );
                  })
                )}
              </div>
            </div>

            {/* Assignment Preview */}
            {selectedUserForManager && (
              <div className="bg-indigo-50 border border-indigo-200 rounded-md p-3 text-xs space-y-1">
                <div className="font-semibold text-indigo-900 flex items-center gap-1.5">
                  <CheckCircle2 className="h-4 w-4 text-indigo-600" />
                  Assignment Preview
                </div>
                <div className="text-gray-700">
                  <span className="text-gray-500">Selected Venue: </span>
                  <strong className="text-gray-900">{selectedVenue.name} ({selectedVenue.code})</strong>
                </div>
                <div className="text-gray-700">
                  <span className="text-gray-500">Selected Account: </span>
                  <strong className="text-gray-900">{selectedUserForManager.email}</strong>
                  <span className="ml-2 px-1.5 py-0.2 rounded text-[10px] bg-blue-100 text-blue-800 font-medium">
                    FacilityAdmin
                  </span>
                </div>
              </div>
            )}

            {/* API Error Display */}
            {assignManagerError && (
              <div className="p-3 bg-red-50 border border-red-200 text-xs text-red-700 rounded-md flex items-center gap-2">
                <AlertTriangle className="h-4 w-4 text-red-600 shrink-0" />
                <span>{assignManagerError}</span>
              </div>
            )}

            {/* Actions */}
            <div className="flex items-center justify-end gap-2 pt-2 border-t border-gray-100">
              <button
                type="button"
                onClick={handleCloseAssignModal}
                disabled={isAssigningManager}
                className="px-3 py-1.5 text-xs font-medium rounded-md text-gray-700 hover:bg-gray-100"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={handleAssignManager}
                disabled={!selectedUserForManager || isAssigningManager}
                className="px-4 py-1.5 text-xs font-semibold rounded-md bg-indigo-600 text-white hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-1.5"
              >
                {isAssigningManager && <RefreshCw className="h-3.5 w-3.5 animate-spin" />}
                {isAssigningManager ? 'Assigning...' : 'Assign Manager'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
