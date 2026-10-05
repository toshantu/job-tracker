export type ApplicationStatus =
  | "Wishlist"
  | "Applied"
  | "Interviewing"
  | "Offer"
  | "Accepted"
  | "Rejected"
  | "Withdrawn";

export type InterviewOutcome = "Pending" | "Passed" | "Failed" | "Cancelled";

export type DocumentType = "Cv" | "CoverLetter";

export interface CurrentUser {
  userId: string;
  email: string | null;
  displayName: string | null;
  isAdmin: boolean;
}

export interface InterviewStage {
  id: number;
  stageName: string;
  scheduledAt: string | null;
  outcome: InterviewOutcome;
  notes: string | null;
}

export interface ApplicationDocument {
  id: number;
  type: DocumentType;
  label: string;
  notes: string | null;
}

export interface JobApplication {
  id: number;
  company: string;
  roleTitle: string;
  status: ApplicationStatus;
  dateApplied: string;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
  interviewStages: InterviewStage[];
  documents: ApplicationDocument[];
}

export type ApiResult<T> =
  | { kind: "ok"; data: T }
  | { kind: "unauthorized" }
  | { kind: "error"; message: string };

// Never rejects: callers check signal.aborted after awaiting instead of catching.
async function getJson<T>(path: string, signal: AbortSignal): Promise<ApiResult<T>> {
  let response: Response;
  try {
    response = await fetch(path, { cache: "no-store", signal });
  } catch {
    return { kind: "error", message: "Could not reach the server." };
  }

  if (response.status === 401) {
    return { kind: "unauthorized" };
  }
  if (!response.ok) {
    return { kind: "error", message: `The server responded with HTTP ${response.status}.` };
  }

  try {
    return { kind: "ok", data: (await response.json()) as T };
  } catch {
    return { kind: "error", message: "The server sent an unexpected response." };
  }
}

export function fetchCurrentUser(signal: AbortSignal): Promise<ApiResult<CurrentUser>> {
  return getJson<CurrentUser>("/api/auth/me", signal);
}

export function fetchJobApplications(signal: AbortSignal): Promise<ApiResult<JobApplication[]>> {
  return getJson<JobApplication[]>("/api/job-applications", signal);
}

export async function logout(): Promise<boolean> {
  try {
    const response = await fetch("/api/auth/logout", { method: "POST", cache: "no-store" });
    return response.ok;
  } catch {
    return false;
  }
}
