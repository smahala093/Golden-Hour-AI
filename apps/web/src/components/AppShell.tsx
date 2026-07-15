import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, NavLink, useLocation } from 'react-router-dom';
import { AlertCircle, Clock3, HeartPulse, Home, Menu, Phone, Settings, ShieldCheck, Wifi, WifiOff, X } from 'lucide-react';
import { MOCK_MODE } from '../api';
import { flushQueuedUpdates } from '../offline';
import { useAppState } from '../state';

export function emergencyNumber(): string {
  const configured = (import.meta.env.VITE_EMERGENCY_NUMBER as string | undefined) || '112';
  return /^\+?[0-9]{2,15}$/.test(configured) ? configured : '112';
}

function EmergencyCallDock() {
  const { t } = useTranslation();
  const [announcement, setAnnouncement] = useState('');
  const number = emergencyNumber();

  const handleCall = () => {
    setAnnouncement(t('action.callInitiated'));
  };

  return (
    <div className="call-dock" aria-label={t('common.callEmergency')}>
      <a className="call-dock__button" href={`tel:${number}`} onClick={handleCall} aria-describedby="call-dock-note">
        <Phone aria-hidden="true" />
        <span>{t('common.call', { number })}</span>
      </a>
      <span id="call-dock-note" className="sr-only">{t('action.noClaim')}</span>
      <span className="sr-only" role="status" aria-live="assertive">{announcement}</span>
    </div>
  );
}

function StatusStrip() {
  const { t } = useTranslation();
  const { online, queueCount, refreshQueueCount } = useAppState();

  useEffect(() => {
    if (!online) return;
    void flushQueuedUpdates((import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '').then(refreshQueueCount);
  }, [online, refreshQueueCount]);

  return (
    <div className={`status-strip ${online ? 'status-strip--online' : 'status-strip--offline'}`} role="status" aria-live="polite">
      {online ? <Wifi aria-hidden="true" size={16} /> : <WifiOff aria-hidden="true" size={16} />}
      <span>{online ? t('status.online') : t('status.offline')}</span>
      {queueCount > 0 && <span>· {t('offline.queue', { count: queueCount })}</span>}
      {MOCK_MODE && <span className="demo-pill">{t('common.demoLabel')}</span>}
    </div>
  );
}

const navigation = [
  { to: '/home', key: 'nav.home', icon: Home },
  { to: '/history', key: 'nav.history', icon: Clock3 },
  { to: '/readiness', key: 'nav.readiness', icon: ShieldCheck },
  { to: '/settings', key: 'nav.settings', icon: Settings },
] as const;

export function AppShell({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const location = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);
  const mainRef = useRef<HTMLElement>(null);

  useEffect(() => {
    setMenuOpen(false);
    mainRef.current?.focus();
  }, [location.pathname]);

  return (
    <div className="app-frame">
      <StatusStrip />
      <header className="app-header">
        <Link className="brand" to="/home" aria-label={`${t('common.appName')} · ${t('nav.home')}`}>
          <span className="brand__mark" aria-hidden="true"><HeartPulse /></span>
          <span><strong>{t('common.appName')}</strong><small>{t('common.tagline')}</small></span>
        </Link>
        <button className="icon-button menu-button" type="button" aria-expanded={menuOpen} aria-controls="primary-navigation" aria-label={menuOpen ? t('nav.closeMenu') : t('nav.menu')} onClick={() => setMenuOpen((open) => !open)}>
          {menuOpen ? <X aria-hidden="true" /> : <Menu aria-hidden="true" />}
        </button>
        <nav id="primary-navigation" className={`primary-nav ${menuOpen ? 'primary-nav--open' : ''}`} aria-label="Primary navigation">
          {navigation.map(({ to, key, icon: Icon }) => (
            <NavLink key={to} to={to} className={({ isActive }) => isActive ? 'nav-link nav-link--active' : 'nav-link'}>
              <Icon aria-hidden="true" size={20} /><span>{t(key)}</span>
            </NavLink>
          ))}
        </nav>
      </header>
      <main id="main-content" className="main-content" ref={mainRef} tabIndex={-1}>{children}</main>
      <footer className="app-footer">
        <AlertCircle aria-hidden="true" size={18} />
        <span>{t('landing.safety')}</span>
      </footer>
      <EmergencyCallDock />
    </div>
  );
}

export function PublicShell({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  return (
    <div className="public-frame">
      <StatusStrip />
      <header className="public-header"><Link className="brand" to="/"><span className="brand__mark" aria-hidden="true"><HeartPulse /></span><strong>{t('common.appName')}</strong></Link></header>
      <main id="main-content" className="main-content" tabIndex={-1}>{children}</main>
      <EmergencyCallDock />
    </div>
  );
}

export function PageHeading({ eyebrow, title, description, children }: { eyebrow?: string; title: string; description?: string; children?: ReactNode }) {
  return (
    <header className="page-heading">
      {eyebrow && <p className="eyebrow">{eyebrow}</p>}
      <div className="page-heading__row"><div><h1>{title}</h1>{description && <p>{description}</p>}</div>{children}</div>
    </header>
  );
}

export function ConnectionBanner({ state }: { state: 'connecting' | 'connected' | 'polling' | 'offline' }) {
  const { t } = useTranslation();
  const label = state === 'connected' ? t('status.connected') : state === 'connecting' ? t('status.connecting') : state === 'offline' ? t('status.offline') : t('status.polling');
  return <div className={`connection-banner connection-banner--${state}`} role="status" aria-live="polite">{state === 'connected' ? <Wifi aria-hidden="true" /> : <WifiOff aria-hidden="true" />}<span>{label}</span></div>;
}

export function SourceBadge({ source }: { source: string }) {
  return <span className={`source-badge source-badge--${source}`}>{source.replace('-', ' ')}</span>;
}
