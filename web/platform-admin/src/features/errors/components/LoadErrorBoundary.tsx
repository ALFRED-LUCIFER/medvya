import { Component, type ReactNode } from 'react';

/**
 * A screen that failed to render or whose code failed to download (an old tab after a release) shows `fallback` instead of a blank page.
 * React keeps a failed lazy import failed, so only a reload recovers. Same as the clinic's boundary (apps cannot import each other).
 */
export default class LoadErrorBoundary extends Component<{ fallback: ReactNode; children: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  render() { return this.state.failed ? this.props.fallback : this.props.children; }
}
