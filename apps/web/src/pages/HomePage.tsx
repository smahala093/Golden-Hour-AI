import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { ArrowRight, ContactRound, FileHeart, MapPin, Mic, ShieldCheck, Users } from 'lucide-react';
import { api } from '../api';
import { emergencyNumber, PageHeading } from '../components/AppShell';
import { useAppState } from '../state';

export function HomePage() {
  const { t } = useTranslation();
  const { preferences, session, profile } = useAppState();
  const readiness = useQuery({ queryKey: ['readiness-home'], queryFn: () => api.getReadiness(), retry: 1 });
  const sessions = useQuery({ queryKey: ['sessions'], queryFn: () => api.listSessions(), retry: 1 });
  const activeSession = sessions.data?.find((item) => item.status !== 'closed') ?? (session.id ? session : null);
  const score = readiness.data?.score ?? 0;
  const greetingName = profile.name ? `, ${profile.name.split(' ')[0]}` : '';
  const number = emergencyNumber();

  if (preferences.simpleMode) {
    const activeTask = activeSession?.tasks.find((task) => task.status !== 'completed');
    return (
      <div className="stack">
        <PageHeading title={t('home.greeting', { name: greetingName })} description={t('home.ready', { score })} />
        <section className="emergency-hero">
          <Link className="emergency-button" to="/emergency/start">{t('home.emergency')}</Link>
          <p className="emergency-hint">{t('home.emergencyHint', { number })}</p>
          <Link className="button button--secondary" to="/emergency/start"><Mic aria-hidden="true" />{t('capture.speak')}</Link>
        </section>
        <div className="two-column">
          <article className="card"><p className="eyebrow">{t('home.simpleTask')}</p><h2>{activeTask?.title ?? t('tasks.empty')}</h2></article>
          <article className="card"><p className="eyebrow">{t('home.familyStatus')}</p><h2>{activeSession?.participants.length ?? 0}</h2></article>
        </div>
      </div>
    );
  }

  return (
    <div>
      <PageHeading title={t('home.greeting', { name: greetingName })} description={t('home.ready', { score })}>
        <Link className="button button--secondary" to="/readiness"><ShieldCheck aria-hidden="true" />{t('nav.readiness')}</Link>
      </PageHeading>
      <section className="emergency-hero" aria-labelledby="emergency-start-title">
        <Link id="emergency-start-title" className="emergency-button" to="/emergency/start">{t('home.emergency')}</Link>
        <p className="emergency-hint">{t('home.emergencyHint', { number })}</p>
      </section>
      {activeSession && <section className="section">
        <div className="section-heading"><h2>{t('home.active')}</h2></div>
        <article className="card card--raised">
          <div className="page-heading__row">
            <div><p className="eyebrow">{activeSession.id}</p><h3>{activeSession.patient || t('common.unknown')} · {t(`category.${activeSession.category === 'chest-pain' ? 'chestPain' : activeSession.category === 'fall-injury' ? 'fallInjury' : 'unknown'}`)}</h3><p><MapPin aria-hidden="true" size={16} /> {activeSession.location || t('common.unknown')}</p></div>
            <Link className="button" to={`/emergency/${activeSession.id}`}>{t('home.resume')}<ArrowRight aria-hidden="true" /></Link>
          </div>
        </article>
      </section>}
      <section className="section" aria-labelledby="prepare-heading">
        <div className="section-heading"><h2 id="prepare-heading">{t('home.quick')}</h2></div>
        <div className="three-column">
          <Link className="card card--raised nav-link" to="/profile"><FileHeart aria-hidden="true" /><span><strong>{t('home.card')}</strong><small>{t('common.selfReported')}</small></span></Link>
          <Link className="card card--raised nav-link" to="/contacts"><ContactRound aria-hidden="true" /><span><strong>{t('home.contacts')}</strong><small>{profile.contacts.length}</small></span></Link>
          <Link className="card card--raised nav-link" to="/privacy"><Users aria-hidden="true" /><span><strong>{t('home.sharing')}</strong><small>{profile.shareFields.length}</small></span></Link>
        </div>
      </section>
    </div>
  );
}
