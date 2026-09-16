import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach, vi } from "vitest";

// Testing Library only cleans up on its own when test globals are switched on.
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

// jsdom lacks the browser APIs Mantine uses to follow the colour scheme and measure layout.
Object.defineProperty(window, "matchMedia", {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }),
});

window.HTMLElement.prototype.scrollIntoView = () => {};

class NoResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}
window.ResizeObserver = NoResizeObserver as unknown as typeof ResizeObserver;
