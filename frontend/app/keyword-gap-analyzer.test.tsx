import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { KeywordGapAnalyzer } from "./keyword-gap-analyzer";
import {
  CV_TEXT,
  JOB_DESCRIPTION,
  SAMPLE_KEYWORDS,
  deferred,
  fakeFetch,
  json,
  keyword,
  plainText,
  reportBody,
} from "@/test/support";

const ROUTE = "POST /api/ai/keyword-gap";

type User = ReturnType<typeof userEvent.setup>;

function setup(props: { slowAfterMs?: number } = {}) {
  const onSignedOut = vi.fn();
  const user = userEvent.setup();
  const view = render(<KeywordGapAnalyzer onSignedOut={onSignedOut} {...props} />);
  return { user, onSignedOut, view };
}

const jobBox = () => screen.getByLabelText<HTMLTextAreaElement>("Job description");
const cvBox = () => screen.getByLabelText<HTMLTextAreaElement>("CV");
const analyzeButton = () => screen.getByRole<HTMLButtonElement>("button", { name: /^analy/i });
const clearButton = () => screen.getByRole<HTMLButtonElement>("button", { name: "Clear" });
const resultsHeading = () => screen.queryByRole("heading", { name: "Keyword overlap" });

async function paste(user: User, box: HTMLElement, text: string) {
  await user.click(box);
  await user.paste(text);
}

async function fillBoth(user: User, job = JOB_DESCRIPTION, cv = CV_TEXT) {
  await paste(user, jobBox(), job);
  await paste(user, cvBox(), cv);
}

// The items of the list under a heading, as the text a reader would see, for example "Docker Preferred".
function itemsUnder(heading: string): string[] {
  const section = screen.getByRole("heading", { name: heading }).parentElement;
  if (!section) throw new Error(`No section for ${heading}`);
  return within(section)
    .getAllByRole("listitem")
    .map((item) => item.textContent ?? "");
}

function sleep(ms: number) {
  return new Promise((done) => setTimeout(done, ms));
}

describe("KeywordGapAnalyzer: first look", () => {
  it("says what it does and what happens to the text", () => {
    setup();

    expect(screen.getByRole("heading", { name: "Check a job description against your CV" })).toBeTruthy();

    const notice = screen.getByText(/sent to this app's server and then to an AI provider/);
    expect(notice.textContent).toContain("tries to remove links, email addresses and phone numbers from your CV");
    expect(notice.textContent).toContain("it can miss some");
    expect(notice.textContent).toContain("it does not remove names, employers, addresses or other details");
    expect(notice.textContent).toContain("This app does not store either text");
    expect(notice.textContent).toContain("zero data retention");

    // The notice is tied to the CV box, so it is read out when the box gets focus.
    const describedBy = (cvBox().getAttribute("aria-describedby") ?? "").split(" ");
    expect(describedBy).toContain(notice.id);
  });

  it("asks the browser not to autofill or spell-check what is pasted", () => {
    setup();

    for (const box of [jobBox(), cvBox()]) {
      expect(box.getAttribute("autocomplete")).toBe("off");
      expect(box.getAttribute("spellcheck")).toBe("false");
    }
  });

  it("starts with two empty, labelled boxes and nothing to analyze or clear", () => {
    setup();

    expect(jobBox().value).toBe("");
    expect(cvBox().value).toBe("");
    expect(screen.getByText("0 of 6,000 characters")).toBeTruthy();
    expect(screen.getByText("0 of 7,000 characters")).toBeTruthy();
    expect(analyzeButton().disabled).toBe(true);
    expect(clearButton().disabled).toBe(true);
    expect(resultsHeading()).toBeNull();
    expect(screen.queryByRole("status")).toBeNull();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("needs real text in both boxes before it can analyze", async () => {
    const { user } = setup();

    await paste(user, jobBox(), JOB_DESCRIPTION);
    expect(analyzeButton().disabled).toBe(true);

    await paste(user, cvBox(), "   \n  ");
    expect(analyzeButton().disabled).toBe(true);

    await paste(user, cvBox(), CV_TEXT);
    expect(analyzeButton().disabled).toBe(false);
  });

  it("counts characters and allows exactly the limit", async () => {
    const { user } = setup();

    await paste(user, jobBox(), "a".repeat(6000));
    await paste(user, cvBox(), "b".repeat(7000));

    expect(screen.getByText("6,000 of 6,000 characters")).toBeTruthy();
    expect(screen.getByText("7,000 of 7,000 characters")).toBeTruthy();
    expect(jobBox().getAttribute("aria-invalid")).toBe("false");
    expect(cvBox().getAttribute("aria-invalid")).toBe("false");
    expect(analyzeButton().disabled).toBe(false);
  });

  it("blocks a job description that is one character too long, and says so", async () => {
    const { user } = setup();

    await fillBoth(user, "a".repeat(6001), CV_TEXT);

    expect(screen.getByText("6,001 of 6,000 characters. Too long: shorten it to analyze.")).toBeTruthy();
    expect(jobBox().getAttribute("aria-invalid")).toBe("true");
    expect(cvBox().getAttribute("aria-invalid")).toBe("false");
    expect(analyzeButton().disabled).toBe(true);
  });

  it("blocks a CV that is one character too long, and says so", async () => {
    const { user } = setup();

    await fillBoth(user, JOB_DESCRIPTION, "b".repeat(7001));

    expect(screen.getByText("7,001 of 7,000 characters. Too long: shorten it to analyze.")).toBeTruthy();
    expect(cvBox().getAttribute("aria-invalid")).toBe("true");
    expect(jobBox().getAttribute("aria-invalid")).toBe("false");
    expect(analyzeButton().disabled).toBe(true);
  });
});

describe("KeywordGapAnalyzer: a successful analysis", () => {
  it("sends both texts and shows the overlap, missing keywords first, Required before Preferred", async () => {
    const { requests } = fakeFetch({ [ROUTE]: () => json(200, reportBody(SAMPLE_KEYWORDS)) });
    const { user, onSignedOut } = setup();
    await fillBoth(user);

    await user.click(analyzeButton());
    await screen.findByRole("heading", { name: "Keyword overlap" });

    expect(requests).toHaveLength(1);
    expect(requests[0].body).toEqual({ jobDescription: JOB_DESCRIPTION, cvText: CV_TEXT });

    expect(screen.getByText("Required keywords: 4 of 5 found in your CV (80%)")).toBeTruthy();
    expect(screen.getByText("Preferred keywords: 0 of 2 found in your CV (0%)")).toBeTruthy();
    expect(itemsUnder("Missing from your CV")).toEqual(["GraphQL Required", "Docker Preferred", "Kubernetes Preferred"]);
    expect(itemsUnder("Found in your CV")).toEqual([
      "React Required",
      "TypeScript Required",
      "REST APIs Required",
      "Git Required",
    ]);

    expect(onSignedOut).not.toHaveBeenCalled();
    expect(screen.queryByRole("status")).toBeNull();
    expect(screen.queryByRole("alert")).toBeNull();
    expect(analyzeButton().textContent).toBe("Analyze");
    expect(analyzeButton().disabled).toBe(false);
  });

  it("frames the numbers as keyword overlap and shows no single score", async () => {
    fakeFetch({ [ROUTE]: () => json(200, reportBody(SAMPLE_KEYWORDS)) });
    const { user } = setup();
    await fillBoth(user);

    await user.click(analyzeButton());
    await screen.findByRole("heading", { name: "Keyword overlap" });

    expect(screen.getByText(/not a prediction of how an employer will respond/)).toBeTruthy();
    expect(screen.queryByText(/score|match rate|chance|odds/i)).toBeNull();
    // Exactly two percentages are on the page: one per group, never a combined one.
    expect(document.body.textContent?.match(/\d+%/g)).toHaveLength(2);
  });

  it("says so when nothing is missing", async () => {
    fakeFetch({ [ROUTE]: () => json(200, reportBody([keyword("React", "Required", true)])) });
    const { user } = setup();
    await fillBoth(user);

    await user.click(analyzeButton());
    await screen.findByRole("heading", { name: "Keyword overlap" });

    expect(screen.getByText("Nothing is missing: every keyword was found in your CV.")).toBeTruthy();
    expect(itemsUnder("Found in your CV")).toEqual(["React Required"]);
    expect(screen.getByText("Required keywords: 1 of 1 found in your CV (100%)")).toBeTruthy();
    expect(screen.getByText("Preferred keywords: none listed in the job description")).toBeTruthy();
  });

  it("says so when none of the keywords were found", async () => {
    fakeFetch({ [ROUTE]: () => json(200, reportBody([keyword("Docker", "Preferred", false)])) });
    const { user } = setup();
    await fillBoth(user);

    await user.click(analyzeButton());
    await screen.findByRole("heading", { name: "Keyword overlap" });

    expect(screen.getByText("None of the keywords were found in your CV.")).toBeTruthy();
    expect(itemsUnder("Missing from your CV")).toEqual(["Docker Preferred"]);
    expect(screen.getByText("Preferred keywords: 0 of 1 found in your CV (0%)")).toBeTruthy();
    expect(screen.getByText("Required keywords: none listed in the job description")).toBeTruthy();
  });

  it("says so when the job description yielded no keywords", async () => {
    fakeFetch({ [ROUTE]: () => json(200, reportBody([])) });
    const { user } = setup();
    await fillBoth(user);

    await user.click(analyzeButton());
    await screen.findByRole("heading", { name: "Keyword overlap" });

    expect(screen.getByText("No keywords were found in the job description.")).toBeTruthy();
    expect(screen.queryByRole("heading", { name: "Missing from your CV" })).toBeNull();
    expect(screen.queryByRole("heading", { name: "Found in your CV" })).toBeNull();
  });

  it("shows keywords as plain text, never as HTML", async () => {
    const hostile = [
      keyword('<img src=x onerror="window.pwned = true">', "Required", false),
      keyword("<b>bold</b>", "Preferred", true),
    ];
    fakeFetch({ [ROUTE]: () => json(200, reportBody(hostile)) });
    const { user } = setup();
    await fillBoth(user);

    await user.click(analyzeButton());
    await screen.findByRole("heading", { name: "Keyword overlap" });

    expect(itemsUnder("Missing from your CV")).toEqual(['<img src=x onerror="window.pwned = true"> Required']);
    expect(itemsUnder("Found in your CV")).toEqual(["<b>bold</b> Preferred"]);
    expect(document.querySelector("img")).toBeNull();
    expect(document.querySelector("li b")).toBeNull();
  });

  it("keeps the texts out of storage, cookies and the address bar", async () => {
    fakeFetch({ [ROUTE]: () => json(200, reportBody(SAMPLE_KEYWORDS)) });
    const before = window.location.href;
    const { user } = setup();
    await fillBoth(user);

    await user.click(analyzeButton());
    await screen.findByRole("heading", { name: "Keyword overlap" });

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(document.cookie).toBe("");
    expect(window.location.href).toBe(before);
  });
});

describe("KeywordGapAnalyzer: while it waits", () => {
  it("shows progress, locks Analyze, and sends one request even on a double click", async () => {
    const gate = deferred<Response>();
    const { requests } = fakeFetch({ [ROUTE]: () => gate.promise });
    const { user } = setup();
    await fillBoth(user);

    await user.dblClick(analyzeButton());

    expect(requests).toHaveLength(1);
    expect(analyzeButton().textContent).toBe("Analyzing...");
    expect(analyzeButton().disabled).toBe(true);
    expect(screen.getByRole("status").textContent).toContain("Comparing your CV with the job description");
    // Clear stays available, because it is how a person gives up waiting.
    expect(clearButton().disabled).toBe(false);

    gate.resolve(json(200, reportBody(SAMPLE_KEYWORDS)));
    await screen.findByRole("heading", { name: "Keyword overlap" });

    expect(screen.queryByRole("status")).toBeNull();
    expect(analyzeButton().textContent).toBe("Analyze");
  });

  it("admits when it is slow, and stops saying so once the answer arrives", async () => {
    const gate = deferred<Response>();
    fakeFetch({ [ROUTE]: () => gate.promise });
    const { user } = setup({ slowAfterMs: 400 });
    await fillBoth(user);

    await user.click(analyzeButton());
    expect(screen.getByRole("status").textContent).toContain("Comparing your CV");
    expect(screen.queryByText(/Still working/)).toBeNull();

    const slow = await screen.findByText(/Still working/, undefined, { timeout: 3000 });
    expect(slow.textContent).toContain("waking up from sleep");
    expect(analyzeButton().textContent).toBe("Analyzing...");

    gate.resolve(json(200, reportBody(SAMPLE_KEYWORDS)));
    await screen.findByRole("heading", { name: "Keyword overlap" });
    expect(screen.queryByText(/Still working/)).toBeNull();
  });

  it("does not claim slowness after a quick answer", async () => {
    fakeFetch({ [ROUTE]: () => json(200, reportBody(SAMPLE_KEYWORDS)) });
    const { user } = setup({ slowAfterMs: 50 });
    await fillBoth(user);

    await user.click(analyzeButton());
    await screen.findByRole("heading", { name: "Keyword overlap" });
    await sleep(250);

    expect(screen.queryByRole("status")).toBeNull();
    expect(analyzeButton().textContent).toBe("Analyze");
  });
});

describe("KeywordGapAnalyzer: when it goes wrong", () => {
  const failures = [
    ["invalid_input", 400, {}, "Paste both a job description and a CV, then try again."],
    ["too_large", 413, {}, "That is too much text to send at once."],
    ["rate_limited", 429, { retryAfterSeconds: 150 }, "You have reached the limit for now. Please try again in about 3 minutes."],
    ["rate_limited", 429, { retryAfterSeconds: 45 }, "Please try again in less than a minute."],
    ["rate_limited", 429, { retryAfterSeconds: 60 }, "Please try again in about a minute."],
    ["rate_limited", 429, { retryAfterSeconds: 61 }, "Please try again in about 2 minutes."],
    ["rate_limited", 429, { retryAfterSeconds: 5340 }, "Please try again in about 89 minutes."],
    ["rate_limited", 429, { retryAfterSeconds: 5400 }, "Please try again in about 2 hours."],
    ["ai_busy", 503, { retryAfterSeconds: 20000 }, "Please try again in about 6 hours."],
    ["rate_limited", 429, {}, "Please try again in a little while."],
    ["ai_busy", 503, { retryAfterSeconds: 5 }, "The AI service is busy right now. Please try again in less than a minute."],
    ["ai_timeout", 504, {}, "The AI service took too long to answer. Please try again."],
    ["ai_bad_output", 502, {}, "The AI returned an answer that could not be used. Please try again."],
    ["ai_unavailable", 502, {}, "The AI service is not available right now. Please try again later."],
  ] as const;

  it.each(failures)("explains %s (HTTP %i, %j) in its own words", async (code, status, extra, expected) => {
    fakeFetch({ [ROUTE]: () => json(status, { code, message: "RAW SERVER WORDING", ...extra }) });
    const { user } = setup();
    await fillBoth(user);

    await user.click(analyzeButton());
    const alert = await screen.findByRole("alert");

    expect(alert.textContent).toContain(expected);
    expect(document.body.textContent).not.toContain("RAW SERVER WORDING");
    // The person can fix it and try again without retyping anything.
    expect(analyzeButton().textContent).toBe("Analyze");
    expect(analyzeButton().disabled).toBe(false);
    expect(jobBox().value).toBe(JOB_DESCRIPTION);
    expect(cvBox().value).toBe(CV_TEXT);
    expect(resultsHeading()).toBeNull();
    expect(screen.queryByRole("status")).toBeNull();
  });

  it.each([
    ["jobDescription", "The job description is too long. The limit is 5,000 characters. Shorten it and try again."],
    ["cvText", "The CV is too long. The limit is 5,000 characters. Shorten it and try again."],
  ])("uses the server's own limit when it refuses the %s as too long", async (field, expected) => {
    fakeFetch({ [ROUTE]: () => json(413, { code: "too_long", message: "RAW", field, maxLength: 5000 }) });
    const { user } = setup();
    await fillBoth(user);

    await user.click(analyzeButton());

    expect((await screen.findByRole("alert")).textContent).toBe(expected);
  });

  it("reports a network failure", async () => {
    fakeFetch({
      [ROUTE]: () => {
        throw new TypeError("Failed to fetch");
      },
    });
    const { user } = setup();
    await fillBoth(user);

    await user.click(analyzeButton());

    expect((await screen.findByRole("alert")).textContent).toBe(
      "Could not reach the server. Check your connection and try again.",
    );
  });

  it("does not blame the person's own limit when the hosting layer answers 429", async () => {
    fakeFetch({ [ROUTE]: () => plainText(429, "Too many requests") });
    const { user } = setup();
    await fillBoth(user);

    await user.click(analyzeButton());

    const text = (await screen.findByRole("alert")).textContent ?? "";
    expect(text).toContain("The server is limiting requests right now.");
    expect(text).not.toContain("You have reached the limit");
  });

  it("hands over to sign-in when the session has ended, without an error message", async () => {
    fakeFetch({ [ROUTE]: () => new Response(null, { status: 401 }) });
    const { user, onSignedOut } = setup();
    await fillBoth(user);

    await user.click(analyzeButton());
    await waitFor(() => expect(onSignedOut).toHaveBeenCalledTimes(1));

    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("keeps the earlier results on screen when a later run fails", async () => {
    let calls = 0;
    fakeFetch({
      [ROUTE]: () => {
        calls += 1;
        return calls === 1
          ? json(200, reportBody(SAMPLE_KEYWORDS))
          : json(503, { code: "ai_busy", message: "RAW" });
      },
    });
    const { user } = setup();
    await fillBoth(user);

    await user.click(analyzeButton());
    await screen.findByRole("heading", { name: "Keyword overlap" });
    await user.click(analyzeButton());
    const alert = await screen.findByRole("alert");

    expect(alert.textContent).toContain("The AI service is busy right now.");
    expect(resultsHeading()).not.toBeNull();
    expect(itemsUnder("Found in your CV")).toHaveLength(4);
  });

  it("clears the error when the next run starts", async () => {
    const gate = deferred<Response>();
    let calls = 0;
    fakeFetch({
      [ROUTE]: () => {
        calls += 1;
        return calls === 1 ? json(502, { code: "ai_unavailable", message: "RAW" }) : gate.promise;
      },
    });
    const { user } = setup();
    await fillBoth(user);

    await user.click(analyzeButton());
    await screen.findByRole("alert");
    await user.click(analyzeButton());

    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.getByRole("status")).toBeTruthy();

    gate.resolve(json(200, reportBody(SAMPLE_KEYWORDS)));
    await screen.findByRole("heading", { name: "Keyword overlap" });
  });
});

describe("KeywordGapAnalyzer: results that no longer match the text", () => {
  async function analyzed() {
    fakeFetch({ [ROUTE]: () => json(200, reportBody(SAMPLE_KEYWORDS)) });
    const { user } = setup();
    await fillBoth(user);
    await user.click(analyzeButton());
    await screen.findByRole("heading", { name: "Keyword overlap" });
    return user;
  }

  const staleNote = () => screen.queryByText(/You changed the text after this analysis/);

  it("starts with no warning", async () => {
    await analyzed();

    expect(staleNote()).toBeNull();
  });

  it("warns when the job description is edited, and stops warning when the edit is undone", async () => {
    const user = await analyzed();

    await user.type(jobBox(), "!");
    expect(staleNote()).not.toBeNull();
    expect(resultsHeading()).not.toBeNull();

    await user.type(jobBox(), "{Backspace}");
    expect(staleNote()).toBeNull();
  });

  it("warns when the CV is edited, and stops warning when the edit is undone", async () => {
    const user = await analyzed();

    await user.type(cvBox(), "!");
    expect(staleNote()).not.toBeNull();

    await user.type(cvBox(), "{Backspace}");
    expect(staleNote()).toBeNull();
  });

  it("drops the warning once the new text has been analyzed", async () => {
    const user = await analyzed();

    await user.type(cvBox(), " More text.");
    expect(staleNote()).not.toBeNull();
    await user.click(analyzeButton());

    await waitFor(() => expect(staleNote()).toBeNull());
  });
});

describe("KeywordGapAnalyzer: clearing and leaving", () => {
  it("empties everything and disables itself", async () => {
    fakeFetch({ [ROUTE]: () => json(200, reportBody(SAMPLE_KEYWORDS)) });
    const { user } = setup();
    await fillBoth(user);
    await user.click(analyzeButton());
    await screen.findByRole("heading", { name: "Keyword overlap" });

    await user.click(clearButton());

    expect(jobBox().value).toBe("");
    expect(cvBox().value).toBe("");
    expect(resultsHeading()).toBeNull();
    expect(clearButton().disabled).toBe(true);
    expect(analyzeButton().disabled).toBe(true);
  });

  it("also removes an error message", async () => {
    fakeFetch({ [ROUTE]: () => json(502, { code: "ai_unavailable", message: "RAW" }) });
    const { user } = setup();
    await fillBoth(user);
    await user.click(analyzeButton());
    await screen.findByRole("alert");

    await user.click(clearButton());

    expect(screen.queryByRole("alert")).toBeNull();
    expect(clearButton().disabled).toBe(true);
  });

  it("cancels a request that is still running", async () => {
    const gate = deferred<Response>();
    const { requests } = fakeFetch({ [ROUTE]: () => gate.promise });
    const { user } = setup();
    await fillBoth(user);
    await user.click(analyzeButton());
    const signal = requests[0].signal;
    expect(signal?.aborted).toBe(false);

    await user.click(clearButton());

    expect(signal?.aborted).toBe(true);
    expect(screen.queryByRole("status")).toBeNull();
    expect(screen.queryByRole("alert")).toBeNull();
    expect(jobBox().value).toBe("");
    expect(analyzeButton().textContent).toBe("Analyze");
  });

  it("ignores an answer that lands just after the request was cancelled", async () => {
    const gate = deferred<Response>();
    fakeFetch({ [ROUTE]: () => gate.promise }, { honorAbort: false });
    const { user } = setup();
    await fillBoth(user);
    await user.click(analyzeButton());
    await user.click(clearButton());

    gate.resolve(json(200, reportBody(SAMPLE_KEYWORDS)));
    await sleep(100);

    expect(resultsHeading()).toBeNull();
    expect(screen.queryByRole("alert")).toBeNull();
    expect(jobBox().value).toBe("");
  });

  it("lets a new run start after cancelling, and a late answer to the old run cannot disturb it", async () => {
    const first = deferred<Response>();
    const second = deferred<Response>();
    const gates = [first, second];
    let calls = 0;
    const { requests } = fakeFetch({ [ROUTE]: () => gates[calls++].promise }, { honorAbort: false });
    const { user, view } = setup();
    await fillBoth(user);
    await user.click(analyzeButton());
    await user.click(clearButton());
    await fillBoth(user);
    await user.click(analyzeButton());
    expect(requests).toHaveLength(2);

    first.resolve(json(200, reportBody([keyword("OLD-RESULT", "Required", true)])));
    await sleep(100);
    expect(resultsHeading()).toBeNull();
    expect(screen.getByRole("status")).toBeTruthy();

    // The second run is still the one being tracked: leaving the page cancels it.
    view.unmount();
    expect(requests[1].signal?.aborted).toBe(true);
  });

  it("shows the new answer after cancelling and starting again", async () => {
    const first = deferred<Response>();
    const second = deferred<Response>();
    const gates = [first, second];
    let calls = 0;
    fakeFetch({ [ROUTE]: () => gates[calls++].promise });
    const { user } = setup();
    await fillBoth(user);
    await user.click(analyzeButton());
    await user.click(clearButton());
    await fillBoth(user);
    await user.click(analyzeButton());

    second.resolve(json(200, reportBody(SAMPLE_KEYWORDS)));
    await screen.findByRole("heading", { name: "Keyword overlap" });

    expect(itemsUnder("Found in your CV")).toHaveLength(4);
  });

  it("cancels a request that is still running when the component goes away", async () => {
    const gate = deferred<Response>();
    const { requests } = fakeFetch({ [ROUTE]: () => gate.promise });
    const { user, view, onSignedOut } = setup();
    await fillBoth(user);
    await user.click(analyzeButton());
    const signal = requests[0].signal;
    expect(signal?.aborted).toBe(false);

    view.unmount();

    expect(signal?.aborted).toBe(true);
    await sleep(50);
    expect(onSignedOut).not.toHaveBeenCalled();
  });
});
