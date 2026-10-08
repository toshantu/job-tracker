"use client";

import { useEffect, useId, useRef, useState } from "react";
import {
  analyzeKeywordGap,
  KEYWORD_GAP_MAX_CV_CHARS,
  KEYWORD_GAP_MAX_JOB_DESCRIPTION_CHARS,
  type KeywordGapRejection,
  type KeywordGapReport,
  type KeywordGroupSummary,
  type KeywordResult,
} from "@/lib/api";

// After this long the page admits that something is slow. A sleeping free-tier server can take a minute.
const SLOW_AFTER_MS = 6000;

const PRIVACY_NOTICE =
  "Your CV and the job description are sent to this app's server and then to an AI provider (Groq) to find keywords. " +
  "The server tries to remove links, email addresses and phone numbers from your CV first, but it can miss some, and it does not remove names, employers, addresses or other details, so leave out anything you do not want sent. " +
  "This app does not store either text: they stay on this page until you clear them, leave the page or sign out. " +
  "Groq's zero data retention setting is turned on for this app.";

// Same look as the buttons in page.tsx.
const buttonClass =
  "inline-flex items-center justify-center rounded-md border border-zinc-300 px-4 py-2 text-sm font-medium hover:bg-zinc-100 disabled:opacity-50 dark:border-zinc-700 dark:hover:bg-zinc-900";

const primaryButtonClass =
  "inline-flex items-center justify-center rounded-md bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-700 disabled:opacity-50 dark:bg-zinc-100 dark:text-zinc-900 dark:hover:bg-zinc-300";

type Status =
  | { kind: "idle" }
  | { kind: "loading"; slow: boolean }
  | { kind: "failed"; message: string };

interface AnalysisInput {
  jobDescription: string;
  cvText: string;
}

// The report is kept together with the exact text it was made from, so the page can tell when it is out of date.
interface Analysis {
  report: KeywordGapReport;
  input: AnalysisInput;
}

interface InFlight {
  controller: AbortController;
  slowTimer: ReturnType<typeof setTimeout>;
}

function formatCount(count: number): string {
  return count.toLocaleString("en-US");
}

function waitText(seconds: number | null): string {
  if (seconds === null) return "a little while";
  if (seconds < 60) return "less than a minute";

  const minutes = Math.ceil(seconds / 60);
  if (minutes === 1) return "about a minute";
  if (minutes < 90) return `about ${minutes} minutes`;

  return `about ${Math.ceil(minutes / 60)} hours`;
}

function describeRejection(rejection: KeywordGapRejection): string {
  switch (rejection.code) {
    case "invalid_input":
      return "Paste both a job description and a CV, then try again.";
    case "too_long": {
      const subject =
        rejection.field === "cvText"
          ? "The CV"
          : rejection.field === "jobDescription"
            ? "The job description"
            : "The text";
      const limit =
        rejection.maxLength === null ? "" : ` The limit is ${formatCount(rejection.maxLength)} characters.`;
      return `${subject} is too long.${limit} Shorten it and try again.`;
    }
    case "too_large":
      return "That is too much text to send at once. Shorten the job description or the CV and try again.";
    case "rate_limited":
      return `You have reached the limit for now. Please try again in ${waitText(rejection.retryAfterSeconds)}.`;
    case "ai_busy":
      return `The AI service is busy right now. Please try again in ${waitText(rejection.retryAfterSeconds)}.`;
    case "ai_timeout":
      return "The AI service took too long to answer. Please try again.";
    case "ai_bad_output":
      return "The AI returned an answer that could not be used. Please try again.";
    case "ai_unavailable":
      return "The AI service is not available right now. Please try again later.";
  }
}

function describeGroup(label: string, group: KeywordGroupSummary): string {
  if (group.total === 0) return `${label} keywords: none listed in the job description`;

  const percent = group.percent === null ? "" : ` (${group.percent}%)`;
  return `${label} keywords: ${group.matched} of ${group.total} found in your CV${percent}`;
}

// Required keywords first. The sort is stable, so the server's order is kept within each group.
function requiredFirst(keywords: KeywordResult[]): KeywordResult[] {
  const rank = (item: KeywordResult) => (item.importance === "Required" ? 0 : 1);
  return [...keywords].sort((a, b) => rank(a) - rank(b));
}

function abortRequest(request: InFlight) {
  request.controller.abort();
  clearTimeout(request.slowTimer);
}

export function KeywordGapAnalyzer({
  onSignedOut,
  slowAfterMs = SLOW_AFTER_MS,
}: {
  onSignedOut: () => void;
  slowAfterMs?: number;
}) {
  const baseId = useId();
  const [jobDescription, setJobDescription] = useState("");
  const [cvText, setCvText] = useState("");
  const [status, setStatus] = useState<Status>({ kind: "idle" });
  const [analysis, setAnalysis] = useState<Analysis | null>(null);
  const inFlight = useRef<InFlight | null>(null);

  // Leaving the page, or signing out (which unmounts this component), cancels a request still running.
  useEffect(() => {
    return () => {
      if (inFlight.current) abortRequest(inFlight.current);
    };
  }, []);

  const loading = status.kind === "loading";
  const withinLimits =
    jobDescription.length <= KEYWORD_GAP_MAX_JOB_DESCRIPTION_CHARS &&
    cvText.length <= KEYWORD_GAP_MAX_CV_CHARS;
  const canAnalyze = !loading && jobDescription.trim() !== "" && cvText.trim() !== "" && withinLimits;
  const canClear = jobDescription !== "" || cvText !== "" || analysis !== null || status.kind !== "idle";
  const stale =
    analysis !== null &&
    (analysis.input.jobDescription !== jobDescription || analysis.input.cvText !== cvText);

  async function analyze() {
    // Belt and braces: the button is already disabled while a request runs, so a second one can never start.
    if (inFlight.current || !canAnalyze) return;

    const input: AnalysisInput = { jobDescription, cvText };
    const controller = new AbortController();
    const slowTimer = setTimeout(() => setStatus({ kind: "loading", slow: true }), slowAfterMs);
    inFlight.current = { controller, slowTimer };
    setStatus({ kind: "loading", slow: false });

    const result = await analyzeKeywordGap(input.jobDescription, input.cvText, controller.signal);

    // Cleared or left while waiting: the request was cancelled on purpose, so there is nothing to show.
    if (controller.signal.aborted) return;

    clearTimeout(slowTimer);
    inFlight.current = null;

    switch (result.kind) {
      case "ok":
        setAnalysis({ report: result.report, input });
        setStatus({ kind: "idle" });
        break;
      case "unauthorized":
        // The session ended. The page swaps to the sign-in view, which discards everything typed here.
        setStatus({ kind: "idle" });
        onSignedOut();
        break;
      case "rejected":
        setStatus({ kind: "failed", message: describeRejection(result) });
        break;
      case "error":
        setStatus({ kind: "failed", message: result.message });
        break;
    }
  }

  function clear() {
    if (inFlight.current) {
      abortRequest(inFlight.current);
      inFlight.current = null;
    }
    setJobDescription("");
    setCvText("");
    setAnalysis(null);
    setStatus({ kind: "idle" });
  }

  return (
    <section aria-labelledby={`${baseId}-heading`} className="space-y-4">
      <h2 id={`${baseId}-heading`} className="text-2xl font-semibold tracking-tight">
        Check a job description against your CV
      </h2>
      <p className="text-zinc-600 dark:text-zinc-400">
        Paste a job description and your CV to see which of the posting&apos;s keywords your CV
        already contains and which it is missing.
      </p>
      <p
        id={`${baseId}-privacy`}
        className="rounded-lg border border-zinc-200 p-3 text-sm text-zinc-700 dark:border-zinc-800 dark:text-zinc-300"
      >
        {PRIVACY_NOTICE}
      </p>

      <Field
        id={`${baseId}-job-description`}
        label="Job description"
        value={jobDescription}
        max={KEYWORD_GAP_MAX_JOB_DESCRIPTION_CHARS}
        placeholder="Paste the job description here"
        onChange={setJobDescription}
      />
      <Field
        id={`${baseId}-cv`}
        label="CV"
        value={cvText}
        max={KEYWORD_GAP_MAX_CV_CHARS}
        placeholder="Paste your CV as plain text"
        describedBy={`${baseId}-privacy`}
        onChange={setCvText}
      />

      <div className="flex flex-wrap items-center gap-3">
        <button type="button" onClick={analyze} disabled={!canAnalyze} className={primaryButtonClass}>
          {loading ? "Analyzing..." : "Analyze"}
        </button>
        <button type="button" onClick={clear} disabled={!canClear} className={buttonClass}>
          Clear
        </button>
      </div>

      {status.kind === "loading" && (
        <p role="status" className="text-sm text-zinc-600 dark:text-zinc-400">
          {status.slow
            ? "Still working. The server may be waking up from sleep (free-tier hosting) or the AI service may be busy, so this can take a minute or more. You can keep this page open."
            : "Comparing your CV with the job description. This usually takes a few seconds."}
        </p>
      )}

      {status.kind === "failed" && (
        <p role="alert" className="text-sm text-red-700 dark:text-red-400">
          {status.message}
        </p>
      )}

      {analysis && <KeywordGapResults report={analysis.report} stale={stale} />}
    </section>
  );
}

function Field({
  id,
  label,
  value,
  max,
  placeholder,
  describedBy,
  onChange,
}: {
  id: string;
  label: string;
  value: string;
  max: number;
  placeholder: string;
  describedBy?: string;
  onChange: (value: string) => void;
}) {
  const countId = `${id}-count`;
  const tooLong = value.length > max;

  return (
    <div className="space-y-1">
      <label htmlFor={id} className="block text-sm font-medium">
        {label}
      </label>
      <textarea
        id={id}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        rows={8}
        placeholder={placeholder}
        autoComplete="off"
        spellCheck={false}
        aria-invalid={tooLong}
        aria-describedby={describedBy ? `${countId} ${describedBy}` : countId}
        className={`w-full resize-y rounded-md border bg-transparent p-3 text-sm ${
          tooLong ? "border-red-600 dark:border-red-500" : "border-zinc-300 dark:border-zinc-700"
        }`}
      />
      <p
        id={countId}
        className={`text-xs ${tooLong ? "text-red-700 dark:text-red-400" : "text-zinc-600 dark:text-zinc-400"}`}
      >
        {tooLong
          ? `${formatCount(value.length)} of ${formatCount(max)} characters. Too long: shorten it to analyze.`
          : `${formatCount(value.length)} of ${formatCount(max)} characters`}
      </p>
    </div>
  );
}

function KeywordGapResults({ report, stale }: { report: KeywordGapReport; stale: boolean }) {
  const missing = requiredFirst(report.keywords.filter((item) => !item.inCv));
  const found = requiredFirst(report.keywords.filter((item) => item.inCv));

  return (
    <div className="space-y-5">
      <h3 className="text-xl font-semibold tracking-tight">Keyword overlap</h3>

      {stale && (
        <p className="text-sm text-amber-800 dark:text-amber-300">
          You changed the text after this analysis, so these results may be out of date. Select
          Analyze to update them.
        </p>
      )}

      <p className="text-sm text-zinc-600 dark:text-zinc-400">
        A rough comparison of the keywords in the job description with the words in your CV. It is
        not a prediction of how an employer will respond.
      </p>

      {report.keywords.length === 0 ? (
        <p>No keywords were found in the job description.</p>
      ) : (
        <>
          <ul className="space-y-1 text-sm">
            <li>{describeGroup("Required", report.summary.required)}</li>
            <li>{describeGroup("Preferred", report.summary.preferred)}</li>
          </ul>
          <KeywordList
            heading="Missing from your CV"
            keywords={missing}
            emptyText="Nothing is missing: every keyword was found in your CV."
            tone="missing"
          />
          <KeywordList
            heading="Found in your CV"
            keywords={found}
            emptyText="None of the keywords were found in your CV."
            tone="found"
          />
        </>
      )}
    </div>
  );
}

const chipClasses = {
  missing: "bg-amber-100 text-amber-900 dark:bg-amber-950 dark:text-amber-200",
  found: "bg-emerald-100 text-emerald-900 dark:bg-emerald-950 dark:text-emerald-200",
};

function KeywordList({
  heading,
  keywords,
  emptyText,
  tone,
}: {
  heading: string;
  keywords: KeywordResult[];
  emptyText: string;
  tone: keyof typeof chipClasses;
}) {
  return (
    <div className="space-y-2">
      <h4 className="font-semibold">{heading}</h4>
      {keywords.length === 0 ? (
        <p className="text-sm text-zinc-600 dark:text-zinc-400">{emptyText}</p>
      ) : (
        <ul className="flex flex-wrap gap-2">
          {keywords.map((item, index) => (
            <li
              key={`${index}-${item.keyword}`}
              className={`rounded-full px-3 py-1 text-sm break-words ${chipClasses[tone]}`}
            >
              {item.keyword} <span className="text-xs font-medium">{item.importance}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
