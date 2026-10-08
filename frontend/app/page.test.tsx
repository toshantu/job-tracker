import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import Home from "./page";
import {
  CV_TEXT,
  JOB_DESCRIPTION,
  SAMPLE_KEYWORDS,
  fakeFetch,
  json,
  reportBody,
  type FakeRoute,
} from "@/test/support";

const ME = { userId: "7", email: "jane@example.invalid", displayName: "Jane Example", isAdmin: false };

function signedInRoutes(overrides: Record<string, FakeRoute> = {}): Record<string, FakeRoute> {
  return {
    "GET /api/auth/me": () => json(200, ME),
    "GET /api/job-applications": () => json(200, []),
    "POST /api/auth/logout": () => new Response(null, { status: 204 }),
    "POST /api/ai/keyword-gap": () => json(200, reportBody(SAMPLE_KEYWORDS)),
    ...overrides,
  };
}

const analyzerHeading = () => screen.findByRole("heading", { name: "Check a job description against your CV" });

describe("Home: where the analyzer appears", () => {
  it("is not offered to someone who is signed out", async () => {
    const { requests } = fakeFetch({ "GET /api/auth/me": () => new Response(null, { status: 401 }) });
    render(<Home />);

    expect(await screen.findByRole("link", { name: "Sign in with Google" })).toBeTruthy();

    expect(screen.queryByRole("heading", { name: "Check a job description against your CV" })).toBeNull();
    expect(screen.queryByLabelText("CV")).toBeNull();
    expect(requests.map((request) => `${request.method} ${request.path}`)).toEqual(["GET /api/auth/me"]);
  });

  it("sits below the applications when someone is signed in, so the page still opens with them", async () => {
    fakeFetch(signedInRoutes());
    render(<Home />);

    const applications = await screen.findByRole("heading", { name: "Your applications" });
    const analyzer = await analyzerHeading();

    expect(applications.compareDocumentPosition(analyzer) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.getByText("Jane Example")).toBeTruthy();
    expect(await screen.findByText("No applications yet.")).toBeTruthy();
  });

  it("still works when the applications fail to load", async () => {
    const { requests } = fakeFetch(
      signedInRoutes({ "GET /api/job-applications": () => new Response("boom", { status: 500 }) }),
    );
    const user = userEvent.setup();
    render(<Home />);

    expect((await screen.findByRole("alert")).textContent).toContain("Could not load your applications.");
    await analyzerHeading();
    await user.click(screen.getByLabelText("Job description"));
    await user.paste(JOB_DESCRIPTION);
    await user.click(screen.getByLabelText("CV"));
    await user.paste(CV_TEXT);
    await user.click(screen.getByRole("button", { name: "Analyze" }));

    expect(await screen.findByRole("heading", { name: "Keyword overlap" })).toBeTruthy();
    expect(requests.filter((request) => request.path === "/api/ai/keyword-gap")).toHaveLength(1);
  });
});

describe("Home: the end of a session", () => {
  it("returns to the sign-in view when the session ends during an analysis", async () => {
    fakeFetch(signedInRoutes({ "POST /api/ai/keyword-gap": () => new Response(null, { status: 401 }) }));
    const user = userEvent.setup();
    render(<Home />);
    await analyzerHeading();
    await user.click(screen.getByLabelText("Job description"));
    await user.paste(JOB_DESCRIPTION);
    await user.click(screen.getByLabelText("CV"));
    await user.paste(CV_TEXT);

    await user.click(screen.getByRole("button", { name: "Analyze" }));

    expect(await screen.findByRole("link", { name: "Sign in with GitHub" })).toBeTruthy();
    expect(screen.queryByLabelText("CV")).toBeNull();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("removes the analyzer, and everything typed into it, when the person signs out", async () => {
    fakeFetch(signedInRoutes());
    const user = userEvent.setup();
    render(<Home />);
    await analyzerHeading();
    await user.click(screen.getByLabelText("CV"));
    await user.paste(CV_TEXT);
    expect(screen.getByLabelText<HTMLTextAreaElement>("CV").value).toBe(CV_TEXT);

    await user.click(screen.getByRole("button", { name: "Sign out" }));

    expect(await screen.findByRole("link", { name: "Sign in with Google" })).toBeTruthy();
    expect(screen.queryByLabelText("CV")).toBeNull();
    expect(document.body.textContent).not.toContain(CV_TEXT);
  });
});
