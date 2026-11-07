import { Component, type ReactNode } from 'react';

/**
 * A screen whose code failed to download (the connection dropped first, or an old tab after a release) shows `fallback`
 * instead of unmounting the whole app, offline banner included. React keeps a failed lazy import failed, so only a reload recovers.
 * shortcut: every render error lands here, not only failed downloads; give it its own message when a generic error state exists (F-05).
 */
export default class LoadErrorBoundary extends Component<{ fallback: ReactNode; children: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  render() { return this.state.failed ? this.props.fallback : this.props.children; }
}
