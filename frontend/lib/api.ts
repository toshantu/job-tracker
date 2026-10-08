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

// ---- Keyword-gap analyzer ---------------------------------------------------------------------
// POST /api/ai/keyword-gap compares a job description with a CV. Both texts travel in the request
// body only: never in a URL and never in browser storage. Everything that comes back is untrusted:
// it is checked field by field, unknown fields are dropped, and the UI shows it as plain text.

// These mirror Ai:KeywordGap in backend/appsettings.json so the page can warn before it sends.
// The server stays the authority: if its limits differ, it answers too_long with the real number.
export const KEYWORD_GAP_MAX_JOB_DESCRIPTION_CHARS = 6000;
export const KEYWORD_GAP_MAX_CV_CHARS = 7000;

export type KeywordImportance = "Required" | "Preferred";

export interface KeywordResult {
  keyword: string;
  importance: KeywordImportance;
  inCv: boolean;
}

export interface KeywordGroupSummary {
  total: number;
  matched: number;
  percent: number | null;
}

export interface KeywordGapReport {
  keywords: KeywordResult[];
  summary: { required: KeywordGroupSummary; preferred: KeywordGroupSummary };
}

const KEYWORD_GAP_ERROR_CODES = [
  "invalid_input",
  "too_long",
  "too_large",
  "rate_limited",
  "ai_busy",
  "ai_timeout",
  "ai_bad_output",
  "ai_unavailable",
] as const;

export type KeywordGapErrorCode = (typeof KEYWORD_GAP_ERROR_CODES)[number];

export type KeywordGapField = "jobDescription" | "cvText";

// The server refused or failed and gave one of its stable codes. The UI words the message itself:
// text sent by the server is never shown.
export interface KeywordGapRejection {
  kind: "rejected";
  code: KeywordGapErrorCode;
  retryAfterSeconds: number | null;
  field: KeywordGapField | null;
  maxLength: number | null;
}

export type KeywordGapResult =
  | { kind: "ok"; report: KeywordGapReport }
  | { kind: "unauthorized" }
  | KeywordGapRejection
  | { kind: "error"; message: string };

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isWholeNumber(value: unknown): value is number {
  return typeof value === "number" && Number.isInteger(value) && value >= 0;
}

function parseGroupSummary(value: unknown): KeywordGroupSummary | null {
  if (!isRecord(value)) return null;

  const { total, matched, percent } = value;
  if (!isWholeNumber(total) || !isWholeNumber(matched) || matched > total) return null;
  if (percent !== null && (!isWholeNumber(percent) || percent > 100)) return null;

  return { total, matched, percent };
}

// The numbers must describe the list that came with them.
function summaryMatchesList(
  keywords: KeywordResult[],
  importance: KeywordImportance,
  group: KeywordGroupSummary,
): boolean {
  const ofImportance = keywords.filter((item) => item.importance === importance);
  return (
    group.total === ofImportance.length &&
    group.matched === ofImportance.filter((item) => item.inCv).length
  );
}

function parseKeywordGapReport(value: unknown): KeywordGapReport | null {
  if (!isRecord(value) || !Array.isArray(value.keywords) || !isRecord(value.summary)) return null;

  const keywords: KeywordResult[] = [];
  for (const item of value.keywords) {
    if (!isRecord(item)) return null;

    const { keyword, importance, inCv } = item;
    if (typeof keyword !== "string" || keyword.trim() === "") return null;
    if (importance !== "Required" && importance !== "Preferred") return null;
    if (typeof inCv !== "boolean") return null;

    keywords.push({ keyword, importance, inCv });
  }

  const required = parseGroupSummary(value.summary.required);
  const preferred = parseGroupSummary(value.summary.preferred);
  if (!required || !preferred) return null;
  if (!summaryMatchesList(keywords, "Required", required)) return null;
  if (!summaryMatchesList(keywords, "Preferred", preferred)) return null;

  return { keywords, summary: { required, preferred } };
}

function parseKeywordGapRejection(value: unknown): KeywordGapRejection | null {
  if (!isRecord(value)) return null;

  const code = KEYWORD_GAP_ERROR_CODES.find((known) => known === value.code);
  if (!code) return null;

  const { retryAfterSeconds, field, maxLength } = value;
  return {
    kind: "rejected",
    code,
    retryAfterSeconds: isWholeNumber(retryAfterSeconds) && retryAfterSeconds > 0 ? retryAfterSeconds : null,
    field: field === "jobDescription" || field === "cvText" ? field : null,
    maxLength: isWholeNumber(maxLength) && maxLength > 0 ? maxLength : null,
  };
}

// Never rejects, like getJson: callers check signal.aborted after awaiting instead of catching.
export async function analyzeKeywordGap(
  jobDescription: string,
  cvText: string,
  signal: AbortSignal,
): Promise<KeywordGapResult> {
  let response: Response;
  try {
    response = await fetch("/api/ai/keyword-gap", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ jobDescription, cvText }),
      cache: "no-store",
      signal,
    });
  } catch {
    return { kind: "error", message: "Could not reach the server. Check your connection and try again." };
  }

  if (response.status === 401) {
    return { kind: "unauthorized" };
  }

  let body: unknown = null;
  try {
    body = await response.json();
  } catch {
    // Not JSON (for example a plain-text answer from the hosting layer): the status decides below.
  }

  if (response.ok) {
    const report = parseKeywordGapReport(body);
    return report
      ? { kind: "ok", report }
      : { kind: "error", message: "The server sent an unexpected response. Please try again." };
  }

  const rejection = parseKeywordGapRejection(body);
  if (rejection) return rejection;

  if (response.status === 429) {
    // This app's own limit always arrives as the JSON above, so a bare 429 comes from the hosting layer.
    return {
      kind: "error",
      message: "The server is limiting requests right now. Please wait a minute and try again.",
    };
  }
  return { kind: "error", message: `The server responded with HTTP ${response.status}. Please try again.` };
}
