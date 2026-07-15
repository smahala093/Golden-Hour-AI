import { Component, type ErrorInfo, type ReactNode } from 'react';
import { AlertTriangle } from 'lucide-react';

interface ErrorBoundaryProps { children: ReactNode }
interface ErrorBoundaryState { hasError: boolean }

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  public override state: ErrorBoundaryState = { hasError: false };

  public static getDerivedStateFromError(): ErrorBoundaryState {
    return { hasError: true };
  }

  public override componentDidCatch(_error: Error, _info: ErrorInfo): void {
    // Production telemetry records only a correlation-safe event; never render raw errors or PHI.
  }

  public override render(): ReactNode {
    if (!this.state.hasError) return this.props.children;
    const number = (import.meta.env.VITE_EMERGENCY_NUMBER as string | undefined) || '112';
    return (
      <main id="main-content" className="centered-state" tabIndex={-1}>
        <AlertTriangle aria-hidden="true" size={40} />
        <h1>Something went wrong</h1>
        <p>Your emergency-call option is still available. Reload this page or return home.</p>
        <div className="button-row">
          <a className="button button--danger" href={`tel:${number}`}>Call {number}</a>
          <button className="button button--secondary" type="button" onClick={() => window.location.reload()}>Reload page</button>
          <a className="button button--ghost" href="/home">Return home</a>
        </div>
      </main>
    );
  }
}
