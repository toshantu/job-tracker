"use client";   

import { useState } from "react";

type CheckState = | { phase: "idle" } | {phase: "loading" } | { phase: "done"; ok: boolean;  status: number | null; ms: number; body: string; }; 

export default function HealthCheckPage() {
  const [state, setState] = useState<CheckState>({ phase: "idle" });

  async function runCheck() {
    setState({ phase: "loading" });
    const start = performance.now();
    try {
      const res = await fetch("/api/health/db", { cache: "no-store" });
      const body = await res.text();
      setState({
        phase: "done",
        ok: res.ok,
        status: res.status,
        ms: Math.round(performance.now() - start),
        body,
      });
    } catch (err) {
      const end = performance.now();
      setState({
        phase: "done",
        ok: false,
        status: null,
        ms: Math.round(end - start),
        body: err instanceof Error ? err.message : "Request Failed",
      });
    }
  }

  return (
    <main className="mx-auto max-w-4xl p-8 font-mono">
      <h1 className="text-xl font-semibold">Proxy health check</h1>
      <p className="mt-2 text-sm">
        Calls <code>/api/health/db</code> on this origin. Next.js rewrites it to the backend&apos;s <code>/health/db</code>; the browser never talks to Render directly.
      </p>
      <button
        onClick={runCheck}
        disabled={state.phase === "loading"}
        className="mt-4 rounded border px-4 py-2 disabled:opacity-50"
      >
        {state.phase === "loading" ? "Checking..." : "Run Check"}
      </button>
      <pre className="mt-4 text-sm whitespace-pre-wrap">
        {state.phase === "idle" && "Not run yet."}
        {state.phase === "loading" && "Waiting for a response. A cold Render start can take a while."}
        {state.phase === "done" && `${state.ok ? "OK" : "FAILED"} | HTTP ${state.status ?? "n/a"} | ${state.ms} ms\n${state.body}`}
      </pre>
    </main>
  );
}