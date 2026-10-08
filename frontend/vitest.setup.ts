import { cleanup } from "@testing-library/react";
import { afterEach, beforeEach, vi, type MockInstance } from "vitest";

// A console.error means something is wrong in the code under test (an act() warning, a bad prop,
// a missing key). It fails the test instead of scrolling past.
let consoleError: MockInstance<typeof console.error>;

beforeEach(() => {
  consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();

  const calls = consoleError.mock.calls;
  consoleError.mockRestore();

  if (calls.length > 0) {
    throw new Error(
      `console.error was called ${calls.length} time(s). First call: ${String(calls[0][0]).slice(0, 300)}`,
    );
  }
});
