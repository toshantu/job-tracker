"use client";

import { useCallback, useEffect, useState } from "react";
import {
  fetchCurrentUser,
  fetchJobApplications,
  logout,
  type ApplicationStatus,
  type CurrentUser,
  type JobApplication,
} from "@/lib/api";
import { KeywordGapAnalyzer } from "./keyword-gap-analyzer";

type AuthState =
  | { kind: "loading" }
  | { kind: "signedOut" }
  | { kind: "signedIn"; user: CurrentUser }
  | { kind: "error"; message: string };

type ApplicationsState =
  | { kind: "loading" }
  | { kind: "loaded"; items: JobApplication[] }
  | { kind: "error"; message: string };

const buttonClass =
  "inline-flex items-center justify-center rounded-md border border-zinc-300 px-4 py-2 text-sm font-medium hover:bg-zinc-100 disabled:opacity-50 dark:border-zinc-700 dark:hover:bg-zinc-900";

const statusClasses: Record<ApplicationStatus, string> = {
  Wishlist: "bg-zinc-100 text-zinc-800 dark:bg-zinc-800 dark:text-zinc-200",
  Applied: "bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-200",
  Interviewing: "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200",
  Offer: "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200",
  Accepted: "bg-green-100 text-green-800 dark:bg-green-950 dark:text-green-200",
  Rejected: "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200",
  Withdrawn: "bg-zinc-200 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400",
};

function plural(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? "" : "s"}`;
}

export default function Home() {
  const [auth, setAuth] = useState<AuthState>({ kind: "loading" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    fetchCurrentUser(controller.signal).then((result) => {
      if (controller.signal.aborted) return;
      if (result.kind === "ok") setAuth({ kind: "signedIn", user: result.data });
      else if (result.kind === "unauthorized") setAuth({ kind: "signedOut" });
      else setAuth({ kind: "error", message: result.message });
    });
    return () => controller.abort();
  }, [attempt]);

  const handleSignedOut = useCallback(() => setAuth({ kind: "signedOut" }), []);

  function retry() {
    setAuth({ kind: "loading" });
    setAttempt((n) => n + 1);
  }

  return (
    <main className="mx-auto w-full max-w-3xl flex-1 px-6 py-12">
      {auth.kind === "loading" && (
        <p role="status" className="text-zinc-600 dark:text-zinc-400">
          Connecting to the server. It may be waking up from sleep (free-tier hosting), so the first
          request after a while idle can take up to a minute.
        </p>
      )}

      {auth.kind === "error" && (
        <div role="alert" className="space-y-4">
          <p>Could not check whether you are signed in. {auth.message}</p>
          <button type="button" onClick={retry} className={buttonClass}>
            Retry
          </button>
        </div>
      )}

      {auth.kind === "signedOut" && (
        <div className="space-y-6">
          <h1 className="text-3xl font-semibold tracking-tight">Job Tracker</h1>
          <p className="text-zinc-600 dark:text-zinc-400">
            Sign in to track your job applications, interview stages and documents.
          </p>
          <div className="flex flex-col gap-3 sm:flex-row">
            <a href="/api/auth/google/login" className={buttonClass}>
              Sign in with Google
            </a>
            <a href="/api/auth/github/login" className={buttonClass}>
              Sign in with GitHub
            </a>
          </div>
        </div>
      )}

      {auth.kind === "signedIn" && (
        <SignedInView
          key={auth.user.userId}
          user={auth.user}
          onSignedOut={handleSignedOut}
        />
      )}
    </main>
  );
}

// Holds all signed-in data; unmounting it on sign-out discards that data.
function SignedInView({ user, onSignedOut }: { user: CurrentUser; onSignedOut: () => void }) {
  const [applications, setApplications] = useState<ApplicationsState>({ kind: "loading" });
  const [attempt, setAttempt] = useState(0);
  const [signingOut, setSigningOut] = useState(false);
  const [signOutFailed, setSignOutFailed] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    fetchJobApplications(controller.signal).then((result) => {
      if (controller.signal.aborted) return;
      if (result.kind === "ok") setApplications({ kind: "loaded", items: result.data });
      else if (result.kind === "unauthorized") onSignedOut();
      else setApplications({ kind: "error", message: result.message });
    });
    return () => controller.abort();
  }, [attempt, onSignedOut]);

  function retry() {
    setApplications({ kind: "loading" });
    setAttempt((n) => n + 1);
  }

  async function signOut() {
    setSigningOut(true);
    setSignOutFailed(false);
    if (await logout()) {
      onSignedOut();
    } else {
      setSigningOut(false);
      setSignOutFailed(true);
    }
  }

  const name = user.displayName || user.email || `userId ${user.userId}`;

  return (
    <div className="space-y-8">
      <header className="flex flex-wrap items-center justify-between gap-4">
        <div className="flex items-center gap-2">
          <span className="text-zinc-600 dark:text-zinc-400">Signed in as</span>
          <span className="font-medium">{name}</span>
          {user.isAdmin && (
            <span className="rounded-full bg-purple-100 px-2 py-0.5 text-xs font-medium text-purple-800 dark:bg-purple-950 dark:text-purple-200">
              Admin
            </span>
          )}
        </div>
        <button type="button" onClick={signOut} disabled={signingOut} className={buttonClass}>
          {signingOut ? "Signing out..." : "Sign out"}
        </button>
      </header>

      {signOutFailed && (
        <p role="alert" className="text-sm text-red-700 dark:text-red-400">
          Sign-out failed. You are still signed in. Please try again.
        </p>
      )}

      <section className="space-y-4">
        <h1 className="text-2xl font-semibold tracking-tight">Your applications</h1>

        {applications.kind === "loading" && (
          <p role="status" className="text-zinc-600 dark:text-zinc-400">
            Loading applications...
          </p>
        )}

        {applications.kind === "error" && (
          <div role="alert" className="space-y-3">
            <p>Could not load your applications. {applications.message}</p>
            <button type="button" onClick={retry} className={buttonClass}>
              Retry
            </button>
          </div>
        )}

        {applications.kind === "loaded" && applications.items.length === 0 && (
          <p className="text-zinc-600 dark:text-zinc-400">No applications yet.</p>
        )}

        {applications.kind === "loaded" && applications.items.length > 0 && (
          <ul className="space-y-3">
            {applications.items.map((app) => (
              <li
                key={app.id}
                className="rounded-lg border border-zinc-200 p-4 dark:border-zinc-800"
              >
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div>
                    <p className="text-lg font-semibold">{app.company}</p>
                    <p className="text-zinc-700 dark:text-zinc-300">{app.roleTitle}</p>
                  </div>
                  <span
                    className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${statusClasses[app.status]}`}
                  >
                    {app.status}
                  </span>
                </div>
                <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                  Applied {app.dateApplied} · {plural(app.interviewStages.length, "interview stage")}{" "}
                  · {plural(app.documents.length, "document")}
                </p>
              </li>
            ))}
          </ul>
        )}
      </section>

      <KeywordGapAnalyzer onSignedOut={onSignedOut} />
    </div>
  );
}
