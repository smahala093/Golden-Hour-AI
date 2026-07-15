import { AlertTriangle, LockKeyhole, MapPinOff, WifiOff } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { PageHeading, emergencyNumber } from '../components/AppShell';
import { getMinimalOfflineCard } from '../offline';
import { useAppState } from '../state';

export function OfflinePage() {
  const { t } = useTranslation();
  const { queueCount } = useAppState();
  const card = getMinimalOfflineCard();
  const number = emergencyNumber();
  return (
    <div>
      <PageHeading title={t('offline.title')} description={t('offline.body', { number })}><WifiOff aria-hidden="true" size={40} /></PageHeading>
      <div className="two-column">
        <section className="card"><h2>{t('offline.card')}</h2>{card ? <><p className="eyebrow">{t('common.stale')}</p><p>{t('offline.saved', { time: new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(card.savedAtUtc)) })}</p><dl className="data-list"><div><dt>{t('offline.name')}</dt><dd>{card.name || t('common.unknown')}</dd></div><div><dt>{t('offline.age')}</dt><dd>{card.approximateAge ?? t('common.unknown')}</dd></div><div><dt>{t('offline.allergies')}</dt><dd>{card.criticalAllergies.join(', ') || t('common.unknown')}</dd></div></dl></> : <p>{t('offline.none')}</p>}</section>
        <section className="card"><h2>{t('offline.protocol')}</h2><p>{t('common.protocolDisclaimer')}</p><p>{t('offline.queue', { count: queueCount })}</p><p>{t('offline.privacy')}</p><button className="button button--secondary" type="button" onClick={() => window.location.reload()}>{t('offline.retry')}</button></section>
      </div>
    </div>
  );
}

export function UnauthorizedPage() {
  const { t } = useTranslation();
  return <section className="centered-state"><LockKeyhole aria-hidden="true" size={44} /><h1>{t('errors.unauthorizedTitle')}</h1><p>{t('errors.unauthorizedBody')}</p><Link className="button" to="/login">{t('auth.signIn')}</Link></section>;
}

export function NotFoundPage() {
  const { t } = useTranslation();
  return <section className="centered-state"><MapPinOff aria-hidden="true" size={44} /><h1>{t('errors.notFoundTitle')}</h1><p>{t('errors.notFoundBody')}</p><Link className="button" to="/home">{t('errors.home')}</Link></section>;
}

export function GenericErrorPage() {
  const { t } = useTranslation();
  return <section className="centered-state"><AlertTriangle aria-hidden="true" size={44} /><h1>{t('errors.genericTitle')}</h1><p>{t('errors.genericBody')}</p><div className="button-row"><a className="button button--danger" href={`tel:${emergencyNumber()}`}>{t('common.call', { number: emergencyNumber() })}</a><button className="button button--secondary" onClick={() => window.location.reload()}>{t('errors.reload')}</button></div></section>;
}
