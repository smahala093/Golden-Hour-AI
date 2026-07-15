import { Component, type ReactNode } from 'react';
import { AlertTriangle } from 'lucide-react';
import i18n from '../i18n';

interface ErrorBoundaryProps { children: ReactNode }
interface ErrorBoundaryState { hasError: boolean }

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  public override state: ErrorBoundaryState = { hasError: false };

  public static getDerivedStateFromError(): ErrorBoundaryState {
    return { hasError: true };
  }

  public override componentDidCatch(): void {
    // Production telemetry records only a correlation-safe event; never render raw errors or PHI.
  }

  public override render(): ReactNode {
    if (!this.state.hasError) return this.props.children;
    const number = (import.meta.env.VITE_EMERGENCY_NUMBER as string | undefined) || '112';
    return (
      <main id="main-content" className="centered-state" tabIndex={-1}>
        <AlertTriangle aria-hidden="true" size={40} />
        <h1>{i18n.t('errors.genericTitle')}</h1>
        <p>{i18n.t('errors.genericBody')}</p>
        <div className="button-row">
          <a className="button button--danger" href={`tel:${number}`}>{i18n.t('common.call', { number })}</a>
          <button className="button button--secondary" type="button" onClick={() => window.location.reload()}>{i18n.t('errors.reload')}</button>
          <a className="button button--ghost" href="/home">{i18n.t('errors.home')}</a>
        </div>
      </main>
    );
  }
}
