import { describe, expect, it } from "vitest";
import { analyzeKeywordGap } from "@/lib/api";
import {
  CV_TEXT,
  JOB_DESCRIPTION,
  SAMPLE_KEYWORDS,
  fakeFetch,
  json,
  keyword,
  plainText,
  reportBody,
} from "@/test/support";

const ROUTE = "POST /api/ai/keyword-gap";

function analyze(signal: AbortSignal = new AbortController().signal) {
  return analyzeKeywordGap(JOB_DESCRIPTION, CV_TEXT, signal);
}

describe("analyzeKeywordGap: the request", () => {
  it("posts only the two texts, as JSON, to the same-origin proxy path, without caching", async () => {
    const { requests } = fakeFetch({ [ROUTE]: () => json(200, reportBody(SAMPLE_KEYWORDS)) });
    const controller = new AbortController();

    await analyze(controller.signal);

    expect(requests).toHaveLength(1);
    const [request] = requests;
    expect(request.method).toBe("POST");
    expect(request.path).toBe("/api/ai/keyword-gap");
    expect(request.headers.get("content-type")).toBe("application/json");
    expect(request.cache).toBe("no-store");
    expect(request.signal).toBe(controller.signal);
    // Nothing else travels in the body: in particular no user id of any kind.
    expect(request.body).toEqual({ jobDescription: JOB_DESCRIPTION, cvText: CV_TEXT });
  });

  it("sends the texts exactly as given, without trimming them", async () => {
    const { requests } = fakeFetch({ [ROUTE]: () => json(200, reportBody(SAMPLE_KEYWORDS)) });

    await analyzeKeywordGap("  job  \n", "\n cv ", new AbortController().signal);

    expect(requests[0].body).toEqual({ jobDescription: "  job  \n", cvText: "\n cv " });
  });
});

describe("analyzeKeywordGap: a good answer", () => {
  it("returns the report", async () => {
    fakeFetch({ [ROUTE]: () => json(200, reportBody(SAMPLE_KEYWORDS)) });

    const result = await analyze();

    expect(result).toEqual({ kind: "ok", report: reportBody(SAMPLE_KEYWORDS) });
  });

  it("keeps only the fields it knows, dropping anything extra", async () => {
    const body = reportBody([keyword("React", "Required", true)]);
    fakeFetch({
      [ROUTE]: () =>
        json(200, {
          ...body,
          injected: "<script>alert(1)</script>",
          keywords: [{ ...body.keywords[0], html: "<b>x</b>" }],
        }),
    });

    const result = await analyze();

    expect(result).toEqual({ kind: "ok", report: body });
  });

  it("accepts an empty list, where no percentage exists", async () => {
    fakeFetch({ [ROUTE]: () => json(200, reportBody([])) });

    const result = await analyze();

    expect(result).toEqual({
      kind: "ok",
      report: {
        keywords: [],
        summary: {
          required: { total: 0, matched: 0, percent: null },
          preferred: { total: 0, matched: 0, percent: null },
        },
      },
    });
  });
});

describe("analyzeKeywordGap: an answer it cannot trust", () => {
  // A valid answer to start from. Each case below breaks exactly one thing in it, so a case can only be
  // rejected for the reason it names.
  const base = reportBody([keyword("React", "Required", true), keyword("Docker", "Preferred", false)]);
  const [react, docker] = base.keywords;
  const withKeywords = (keywords: unknown[]) => ({ ...base, keywords });
  const withSummary = (summary: Record<string, unknown>) => ({ ...base, summary: { ...base.summary, ...summary } });
  const noRequired = { total: 0, matched: 0, percent: null };

  const bad: Array<[string, unknown]> = [
    ["a body that is not an object", ["React"]],
    ["a missing keywords list", { summary: base.summary }],
    ["keywords that are not a list", { ...base, keywords: "React" }],
    ["a keyword that is not an object", withKeywords(["React", docker])],
    ["a keyword that is not text", withKeywords([{ ...react, keyword: 7 }, docker])],
    ["a blank keyword", withKeywords([{ ...react, keyword: "   " }, docker])],
    ["an unknown importance", { keywords: [{ ...react, importance: "Optional" }, docker], summary: { required: noRequired, preferred: base.summary.preferred } }],
    ["an inCv that is not a boolean", withKeywords([{ ...react, inCv: "yes" }, docker])],
    ["a missing summary", { keywords: base.keywords }],
    ["a missing preferred group", { ...base, summary: { required: base.summary.required } }],
    ["a required total that does not match the list", withSummary({ required: { total: 2, matched: 1, percent: 50 } })],
    ["a required matched count that does not match the list", withSummary({ required: { total: 1, matched: 0, percent: 0 } })],
    ["a preferred total that does not match the list", withSummary({ preferred: { total: 2, matched: 0, percent: 0 } })],
    ["a preferred matched count that does not match the list", withSummary({ preferred: { total: 1, matched: 1, percent: 100 } })],
    ["matched above total", withSummary({ required: { total: 1, matched: 2, percent: 100 } })],
    ["a percentage above 100", withSummary({ required: { total: 1, matched: 1, percent: 101 } })],
    ["a percentage that is not a whole number", withSummary({ required: { total: 1, matched: 1, percent: 99.5 } })],
    ["a percentage that is missing", withSummary({ required: { total: 1, matched: 1 } })],
    ["a count that is negative", withSummary({ required: { total: -1, matched: 0, percent: null } })],
  ];

  it("accepts the answer the cases below start from", async () => {
    fakeFetch({ [ROUTE]: () => json(200, base) });

    const result = await analyze();

    expect(result).toEqual({ kind: "ok", report: base });
  });

  it.each(bad)("rejects %s", async (_label, body) => {
    fakeFetch({ [ROUTE]: () => json(200, body) });

    const result = await analyze();

    expect(result).toEqual({ kind: "error", message: "The server sent an unexpected response. Please try again." });
  });

  it("rejects a 200 that is not JSON", async () => {
    fakeFetch({ [ROUTE]: () => plainText(200, "OK") });

    const result = await analyze();

    expect(result).toEqual({ kind: "error", message: "The server sent an unexpected response. Please try again." });
  });

  it("does not treat an error-shaped body on a 200 as a rejection", async () => {
    fakeFetch({ [ROUTE]: () => json(200, { code: "rate_limited", message: "x" }) });

    const result = await analyze();

    expect(result.kind).toBe("error");
  });
});

describe("analyzeKeywordGap: the server says no", () => {
  const cases = [
    [400, "invalid_input"],
    [413, "too_large"],
    [413, "too_long"],
    [429, "rate_limited"],
    [503, "ai_busy"],
    [504, "ai_timeout"],
    [502, "ai_bad_output"],
    [502, "ai_unavailable"],
  ] as const;

  it.each(cases)("turns HTTP %i with code %s into a rejection", async (status, code) => {
    fakeFetch({ [ROUTE]: () => json(status, { code, message: "SERVER WORDING" }) });

    const result = await analyze();

    expect(result).toEqual({
      kind: "rejected",
      code,
      retryAfterSeconds: null,
      field: null,
      maxLength: null,
    });
  });

  it("passes on the retry delay for a rate limit", async () => {
    fakeFetch({
      [ROUTE]: () =>
        json(429, { code: "rate_limited", message: "m", retryAfterSeconds: 150 }, { "Retry-After": "150" }),
    });

    const result = await analyze();

    expect(result).toMatchObject({ kind: "rejected", code: "rate_limited", retryAfterSeconds: 150 });
  });

  it("passes on which field was too long and the real limit", async () => {
    fakeFetch({
      [ROUTE]: () => json(413, { code: "too_long", message: "m", field: "cvText", maxLength: 7000 }),
    });

    const result = await analyze();

    expect(result).toMatchObject({ kind: "rejected", code: "too_long", field: "cvText", maxLength: 7000 });
  });

  it("reads null and absent optional fields the same way", async () => {
    fakeFetch({
      [ROUTE]: () =>
        json(429, { code: "rate_limited", message: "m", retryAfterSeconds: null, field: null, maxLength: null }),
    });

    const result = await analyze();

    expect(result).toEqual({
      kind: "rejected",
      code: "rate_limited",
      retryAfterSeconds: null,
      field: null,
      maxLength: null,
    });
  });

  it.each([
    ["zero", 0],
    ["negative", -5],
    ["a fraction", 1.5],
    ["text", "150"],
    ["huge text", "9".repeat(50)],
  ])("ignores a retry delay that is %s", async (_label, value) => {
    fakeFetch({ [ROUTE]: () => json(429, { code: "rate_limited", message: "m", retryAfterSeconds: value }) });

    const result = await analyze();

    expect(result).toMatchObject({ kind: "rejected", retryAfterSeconds: null });
  });

  it("ignores a field name or limit it does not know", async () => {
    fakeFetch({
      [ROUTE]: () => json(413, { code: "too_long", message: "m", field: "somethingElse", maxLength: "lots" }),
    });

    const result = await analyze();

    expect(result).toMatchObject({ kind: "rejected", field: null, maxLength: null });
  });

  it("never passes the server's wording on", async () => {
    fakeFetch({ [ROUTE]: () => json(502, { code: "ai_unavailable", message: "PROVIDER SAID: secret detail" }) });

    const result = await analyze();

    expect(JSON.stringify(result)).not.toContain("PROVIDER SAID");
  });

  it("treats an unknown code as a plain HTTP failure and shows none of its text", async () => {
    fakeFetch({
      [ROUTE]: () => json(500, { code: "weird_new_code", message: '<img src=x onerror="alert(1)">' }),
    });

    const result = await analyze();

    expect(result).toEqual({ kind: "error", message: "The server responded with HTTP 500. Please try again." });
  });
});

describe("analyzeKeywordGap: everything else", () => {
  it("reports an expired session as unauthorized, even with an empty body", async () => {
    fakeFetch({ [ROUTE]: () => new Response(null, { status: 401 }) });

    const result = await analyze();

    expect(result).toEqual({ kind: "unauthorized" });
  });

  it("does not mistake a bare 429 from the hosting layer for this app's own rate limit", async () => {
    fakeFetch({
      [ROUTE]: () => plainText(429, "Too many requests", { "x-render-routing": "hibernate-rate-limited" }),
    });

    const result = await analyze();

    expect(result).toEqual({
      kind: "error",
      message: "The server is limiting requests right now. Please wait a minute and try again.",
    });
  });

  it.each([502, 503, 504])("describes a bare HTTP %i from the hosting layer", async (status) => {
    fakeFetch({ [ROUTE]: () => plainText(status, "<html>Bad gateway</html>") });

    const result = await analyze();

    expect(result).toEqual({ kind: "error", message: `The server responded with HTTP ${status}. Please try again.` });
  });

  it("reports a network failure", async () => {
    fakeFetch({
      [ROUTE]: () => {
        throw new TypeError("Failed to fetch");
      },
    });

    const result = await analyze();

    expect(result).toEqual({ kind: "error", message: "Could not reach the server. Check your connection and try again." });
  });

  it("resolves instead of rejecting when the request is aborted", async () => {
    fakeFetch({ [ROUTE]: () => new Promise<Response>(() => undefined) });
    const controller = new AbortController();

    const pending = analyze(controller.signal);
    controller.abort();

    await expect(pending).resolves.toEqual({ kind: "error", message: "Could not reach the server. Check your connection and try again." });
  });
});
