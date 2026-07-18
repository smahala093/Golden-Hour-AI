import { useState, type FormEvent } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { CheckCircle2, LockKeyhole, Phone } from 'lucide-react';
import { ApiError, MOCK_MODE, api } from '../api';
import { emergencyNumber, PageHeading } from '../components/AppShell';
import { loadCachedProtocol } from './EmergencyFlowPages';
import { useAppState } from '../state';
import { safeParticipantInviteDestination } from '../participantInvite';
import { incidentCategories, type EmergencySession, type IncidentCategory } from '../types';

const publicCategoryKeys: Record<IncidentCategory, string> = { 'chest-pain': 'category.chestPain', 'breathing-difficulty': 'category.breathingDifficulty', 'fall-injury': 'category.fallInjury', unconscious: 'category.unconscious', seizure: 'category.seizure', 'heavy-bleeding': 'category.heavyBleeding', 'road-accident': 'category.roadAccident', 'allergic-reaction': 'category.allergicReaction', 'child-emergency': 'category.childEmergency', unknown: 'category.unknown' };

export function LandingPage() {
  const { t } = useTranslation();
  const { setAuthenticated } = useAppState();
  const navigate = useNavigate();

  const openDemo = () => {
    if (MOCK_MODE) { setAuthenticated(true); void navigate('/home'); }
    else void navigate('/login');
  };

  return (
    <section className="hero">
      <div className="hero__copy">
        <p className="eyebrow">{t('landing.eyebrow')}</p>
        <h1>{t('landing.title')}</h1>
        <p>{t('landing.body')}</p>
        <ul className="hero__features">
          <li><CheckCircle2 aria-hidden="true" />{t('landing.feature1')}</li>
          <li><CheckCircle2 aria-hidden="true" />{t('landing.feature2')}</li>
          <li><CheckCircle2 aria-hidden="true" />{t('landing.feature3')}</li>
        </ul>
        <div className="button-row">
          <button className="button" type="button" onClick={openDemo}>{t('landing.start')}</button>
          <Link className="button button--secondary" to="/help-nearby">{t('landing.nearby')}</Link>
          <Link className="button button--secondary" to="/register">{t('landing.create')}</Link>
          <Link className="button button--ghost" to="/login">{t('landing.signIn')}</Link>
        </div>
      </div>
      <aside className="card card--raised stack" aria-label={t('landing.safetySummary')}>
        <span className="brand__mark" aria-hidden="true"><LockKeyhole /></span>
        <div><p className="eyebrow">{t('landing.privacyEyebrow')}</p><h2>{t('landing.privacyTitle')}</h2></div>
        <p>{t('landing.privacyBody')}</p>
        <p className="safety-note">{t('landing.safety')}</p>
      </aside>
    </section>
  );
}

export function AnonymousEmergencyPage() {
  const { t, i18n } = useTranslation();
  const [category, setCategory] = useState<IncidentCategory>('unknown');
  const [description, setDescription] = useState('');
  const [location, setLocation] = useState('');
  const [session, setSession] = useState<EmergencySession | null>(null);
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);
  const language = (i18n.resolvedLanguage ?? 'en').split('-')[0] ?? 'en';
  const protocolQuery = useQuery({ queryKey: ['public-protocol', session?.category], queryFn: () => loadCachedProtocol(session?.category ?? category), enabled: Boolean(session) && !session?.protocol, staleTime: Infinity });
  const number = emergencyNumber();
  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!description.trim()) { setError(t('auth.invalid')); return; }
    setPending(true); setError('');
    try {
      const created = await api.createAnonymousSession(category, location.trim());
      const updated = await api.submitAnonymousIncident(created.session.id, created.accessToken, description.trim(), category);
      setSession(updated);
    } catch { setError(t('errors.genericBody')); }
    finally { setPending(false); }
  };
  if (session) {
    const protocol = session.protocol ?? protocolQuery.data;
    return <div><PageHeading eyebrow={t('bystander.limited')} title={t('action.title')} description={t('bystander.privateToken')} /><a className="button button--danger button--full" href={`tel:${session.emergencyNumber || number}`}><Phone aria-hidden="true" />{t('common.call', { number: session.emergencyNumber || number })}</a>{language !== 'en' && <p className="permission-note" role="status">{t('action.translationFallback')}</p>}{protocol && <section className="card card--raised section"><h2>{protocol.emergencyCallInstruction}</h2><ol className="plain-list">{protocol.doActions.map((action) => <li key={action.id}><strong>{action.title}</strong><p>{action.detail}</p></li>)}</ol><h3>{t('action.doNot')}</h3><ul className="plain-list">{protocol.doNotActions.map((action) => <li key={action}>{action}</li>)}</ul><h3>{t('action.escalation')}</h3><p>{protocol.escalationRule}</p><p className="disclaimer">{protocol.disclaimer}</p></section>}</div>;
  }
  return <div><PageHeading title={t('landing.nearby')} description={t('bystander.anonymousIntro')} /><form className="form-grid" onSubmit={(event) => void submit(event)}><fieldset className="fieldset"><legend>{t('start.category')}</legend><div className="category-grid">{incidentCategories.map((item) => <label className="choice-card" key={item}><input type="radio" name="public-category" checked={category === item} onChange={() => setCategory(item)} /><span>{t(publicCategoryKeys[item])}</span></label>)}</div></fieldset><div className="field"><label htmlFor="public-description">{t('capture.typed')}</label><textarea id="public-description" value={description} onChange={(event) => setDescription(event.target.value)} maxLength={12_000} required /></div><div className="field"><label htmlFor="public-location">{t('capture.manualLocation')}</label><input id="public-location" value={location} onChange={(event) => setLocation(event.target.value)} maxLength={300} /></div>{error && <p className="field-error" role="alert">{error}</p>}<button className="button" type="submit" disabled={pending}>{pending ? t('common.loading') : t('bystander.submit')}</button></form></div>;
}

interface AuthPageProps { mode: 'login' | 'register' }

export function AuthPage({ mode }: AuthPageProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const { setAuthenticated } = useAppState();
  const inviteDestination = safeParticipantInviteDestination(location.state as unknown);
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError('');
    const data = new FormData(event.currentTarget);
    const value = (key: string) => { const field = data.get(key); return typeof field === 'string' ? field : ''; };
    const email = value('email').trim();
    const password = value('password');
    const name = value('name').trim();
    const confirmation = value('confirmPassword');
    const strongPassword = password.length >= 12 && /[a-z]/.test(password) && /[A-Z]/.test(password) && /\d/.test(password) && /[^A-Za-z0-9]/.test(password);
    if (!email || !strongPassword || (mode === 'register' && (!name || confirmation !== password))) {
      setError(t('auth.invalid'));
      return;
    }
    setPending(true);
    try {
      if (mode === 'login') await api.login(email, password);
      else await api.register(name, email, password);
      setAuthenticated(true);
      void navigate(inviteDestination ?? (mode === 'register' ? '/onboarding' : '/home'), { replace: true });
    } catch (requestError) {
      setError(mode === 'register' && requestError instanceof ApiError ? requestError.message : t('auth.invalid'));
    } finally {
      setPending(false);
    }
  };

  return (
    <section className="auth-layout">
      <div className="card card--raised auth-card">
        <p className="eyebrow">{t('common.appName')}</p>
        <h1>{mode === 'login' ? t('auth.login') : t('auth.register')}</h1>
        <form className="form-grid" onSubmit={(event) => void submit(event)} noValidate>
          {mode === 'register' && <div className="field"><label htmlFor="name">{t('auth.name')}</label><input id="name" name="name" autoComplete="name" required /></div>}
          <div className="field"><label htmlFor="email">{t('auth.email')}</label><input id="email" name="email" type="email" autoComplete="email" defaultValue="demo@goldenhour.ai" required /></div>
          <div className="field"><label htmlFor="password">{t('auth.password')}</label><input id="password" name="password" type="password" autoComplete={mode === 'login' ? 'current-password' : 'new-password'} minLength={12} required /><small>{t('auth.passwordHint')}</small></div>
          {mode === 'register' && <div className="field"><label htmlFor="confirmPassword">{t('auth.confirmPassword')}</label><input id="confirmPassword" name="confirmPassword" type="password" autoComplete="new-password" minLength={12} required /></div>}
          {error && <p className="field-error" role="alert">{error}</p>}
          <button className="button button--full" type="submit" disabled={pending}>{pending ? t('common.loading') : mode === 'login' ? t('auth.signIn') : t('auth.create')}</button>
        </form>
        <p className="field-hint">{t('auth.privacy')}</p>
        <p>{mode === 'login' ? <Link to="/register" state={inviteDestination ? { from: inviteDestination } : undefined}>{t('landing.create')}</Link> : <Link to="/login" state={inviteDestination ? { from: inviteDestination } : undefined}>{t('landing.signIn')}</Link>}</p>
      </div>
    </section>
  );
}
