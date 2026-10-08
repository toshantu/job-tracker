import { vi } from "vitest";
import type { KeywordImportance, KeywordResult } from "@/lib/api";

// ---- Synthetic text. Obviously fake: no real CV or job description is ever used in a test. --------

export const JOB_DESCRIPTION =
  "Acme Example Ltd is hiring a Frontend Engineer. Required: React, TypeScript, REST APIs, Git and GraphQL. Preferred: Docker and Kubernetes.";

export const CV_TEXT =
  "Jane Example. Frontend developer. Built single-page apps with React and TypeScript, consumed REST APIs and used Git every day.";

export function keyword(name: string, importance: KeywordImportance, inCv: boolean): KeywordResult {
  return { keyword: name, importance, inCv };
}

// Required: 4 of 5 found. Preferred: 0 of 2 found. Preferred items come first on purpose,
// because the page is expected to list Required items before Preferred ones.
export const SAMPLE_KEYWORDS: KeywordResult[] = [
  keyword("Docker", "Preferred", false),
  keyword("React", "Required", true),
  keyword("GraphQL", "Required", false),
  keyword("Kubernetes", "Preferred", false),
  keyword("TypeScript", "Required", true),
  keyword("REST APIs", "Required", true),
  keyword("Git", "Required", true),
];

function groupSummary(keywords: KeywordResult[], importance: KeywordImportance) {
  const group = keywords.filter((item) => item.importance === importance);
  const matched = group.filter((item) => item.inCv).length;
  return {
    total: group.length,
    matched,
    percent: group.length === 0 ? null : Math.round((100 * matched) / group.length),
  };
}

// The body the server sends on success, with the counts worked out the way the server works them out.
export function reportBody(keywords: KeywordResult[]) {
  return {
    keywords,
    summary: {
      required: groupSummary(keywords, "Required"),
      preferred: groupSummary(keywords, "Preferred"),
    },
  };
}

// ---- A fake fetch ---------------------------------------------------------------------------------

export interface RecordedRequest {
  method: string;
  path: string;
  headers: Headers;
  cache: RequestCache | undefined;
  body: unknown;
  signal: AbortSignal | null | undefined;
}

export type FakeRoute = (request: RecordedRequest) => Response | Promise<Response>;

export function json(status: number, body: unknown, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json", ...headers },
  });
}

export function plainText(status: number, body: string, headers: Record<string, string> = {}): Response {
  return new Response(body, { status, headers: { "Content-Type": "text/plain", ...headers } });
}

export function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

// Like a real fetch, a pending request fails with an AbortError the moment its signal is aborted.
function rejectOnAbort<T>(promise: Promise<T>, signal: AbortSignal | null | undefined): Promise<T> {
  if (!signal) return promise;

  return new Promise<T>((resolve, reject) => {
    const onAbort = () => reject(new DOMException("The operation was aborted.", "AbortError"));
    if (signal.aborted) {
      onAbort();
      return;
    }
    signal.addEventListener("abort", onAbort, { once: true });
    promise.then(resolve, reject).finally(() => signal.removeEventListener("abort", onAbort));
  });
}

// Replaces the global fetch for one test. Routes are keyed "METHOD /path" and can be swapped while the
// test runs. A request nobody planned for throws, which the code under test sees as "could not reach
// the server", so it cannot slip through unnoticed. With honorAbort false the response still arrives
// after an abort, which is the awkward case of an answer that lands just too late.
export function fakeFetch(routes: Record<string, FakeRoute>, options: { honorAbort?: boolean } = {}) {
  const { honorAbort = true } = options;
  const requests: RecordedRequest[] = [];

  vi.stubGlobal("fetch", async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const request: RecordedRequest = {
      method: (init?.method ?? "GET").toUpperCase(),
      path: typeof input === "string" ? input : input instanceof URL ? input.pathname : input.url,
      headers: new Headers(init?.headers),
      cache: init?.cache,
      body: typeof init?.body === "string" ? JSON.parse(init.body) : undefined,
      signal: init?.signal,
    };
    requests.push(request);

    const route = routes[`${request.method} ${request.path}`];
    if (!route) throw new Error(`No fake route for ${request.method} ${request.path}`);

    const answer = Promise.resolve(route(request));
    return honorAbort ? rejectOnAbort(answer, request.signal) : answer;
  });

  return { requests, routes };
}
