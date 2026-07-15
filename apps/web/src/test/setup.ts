import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

afterEach(() => {
  cleanup();
  window.localStorage.clear();
  window.sessionStorage.clear();
  document.documentElement.lang = 'en';
  document.documentElement.dir = 'ltr';
  document.documentElement.removeAttribute('data-theme');
});

Object.defineProperty(window, 'matchMedia', {
  configurable: true,
  value: (query: string): MediaQueryList => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => undefined,
    removeListener: () => undefined,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    dispatchEvent: () => false,
  }),
});

class TestResizeObserver implements ResizeObserver {
  public disconnect(): void {}
  public observe(): void {}
  public unobserve(): void {}
}

globalThis.ResizeObserver = TestResizeObserver;

// axe-core uses a 2D canvas measurement to distinguish icon ligatures. jsdom
// omits the canvas implementation, so provide only the deterministic text
// measurement surface it needs without disabling any accessibility rule.
HTMLCanvasElement.prototype.getContext = function getContext(this: HTMLCanvasElement, contextId: string) {
  if (contextId !== '2d') return null;
  return {
    canvas: this,
    font: '',
    measureText: (text: string) => ({ width: text.length * 8 }) as TextMetrics,
  } as CanvasRenderingContext2D;
} as typeof HTMLCanvasElement.prototype.getContext;
