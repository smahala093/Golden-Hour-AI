import { useState, type FormEvent, type ReactNode } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
import { Accessibility, ArrowRight, Check, Languages, LockKeyhole, Moon, Plus, ShieldCheck, Sun } from 'lucide-react';
import { api } from '../api';
import { PageHeading } from '../components/AppShell';
import { supportedLanguages } from '../i18n';
import { useAppState } from '../state';
import type { EmergencyContact, EmergencyProfile, Preferences } from '../types';
import { clearUserScopedOfflineData } from '../offline';

function Toggle({ label, hint, checked, onChange }: { label: string; hint?: string; checked: boolean; onChange(value: boolean): void }) {
  return <label className="toggle-row"><span><strong>{label}</strong>{hint && <small>{hint}</small>}</span><span className="switch"><input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} /><span aria-hidden="true" /></span></label>;
}

function Chips({ values }: { values: string[] }) {
  return <ul className="chip-list">{values.length > 0 ? values.map((value) => <li className="chip" key={value}>{value}</li>) : <li className="chip">—</li>}</ul>;
}

export function ProfilePage() {
  const { t } = useTranslation();
  const { profile } = useAppState();
  if (!profile.name) return <section className="centered-state"><UserRoundPlaceholder /><h1>{t('profile.title')}</h1><p>{t('profile.intro')}</p><Link className="button" to="/onboarding">{t('onboarding.title')}</Link></section>;
  return (
    <div>
      <PageHeading title={t('profile.title')} description={t('profile.intro')}><Link className="button button--secondary" to="/onboarding">{t('profile.edit')}</Link></PageHeading>
      <div className="two-column">
        <section className="card card--raised"><h2>{t('profile.personal')}</h2><dl className="data-list"><div><dt>{t('auth.name')}</dt><dd>{profile.name}</dd></div><div><dt>{t('profile.dob')}</dt><dd>{profile.dateOfBirth}</dd></div><div><dt>{t('profile.blood')}</dt><dd>{profile.bloodGroup ?? t('common.unknown')}</dd></div><div><dt>{t('profile.hospital')}</dt><dd>{profile.preferredHospital}</dd></div><div><dt>{t('profile.doctor')}</dt><dd>{profile.doctor}</dd></div></dl></section>
        <section className="card"><p className="eyebrow">{t('common.selfReported')}</p><h2>{t('profile.allergies')}</h2><Chips values={profile.allergies} /><h2 className="section">{t('profile.conditions')}</h2><Chips values={profile.conditions} /><h2 className="section">{t('profile.medicines')}</h2><Chips values={profile.medicines} /><h2 className="section">{t('profile.procedures')}</h2><Chips values={profile.procedures} /></section>
      </div>
      <p className="field-hint section">{t('profile.reviewed')}: {new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(new Date(profile.reviewedAt))}</p>
    </div>
  );
}

export function ContactsPage() {
  const { t } = useTranslation();
  const { profile, setProfile } = useAppState();
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);
  const add = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const value = (key: string) => { const field = data.get(key); return typeof field === 'string' ? field : ''; };
    const contact: EmergencyContact = { id: crypto.randomUUID(), name: value('name'), relationship: value('relationship'), phone: value('phone'), verified: false };
    if (!contact.name || !contact.relationship || !contact.phone) return;
    setPending(true); setError('');
    try { setProfile(await api.updateProfile({ ...profile, contacts: [...profile.contacts, contact] })); setAdding(false); }
    catch { setError(t('errors.genericBody')); }
    finally { setPending(false); }
  };
  return (
    <div>
      <PageHeading title={t('contacts.title')} description={t('contacts.intro')}><button className="button" type="button" onClick={() => setAdding(true)}><Plus aria-hidden="true" />{t('contacts.add')}</button></PageHeading>
      <ul className="card-grid">{profile.contacts.map((contact) => <li className="card card--raised" key={contact.id}><div className="page-heading__row"><div><h2>{contact.name}</h2><p>{contact.relationship} · <a href={`tel:${contact.phone.replace(/[^+0-9]/g, '')}`}>{contact.phone}</a></p></div><span className={`task-status ${contact.verified ? 'task-status--completed' : ''}`}>{contact.verified ? t('contacts.verified') : t('contacts.unverified')}</span></div></li>)}</ul>
      {adding && <form className="card form-grid section" onSubmit={(event) => void add(event)}><div className="field"><label htmlFor="contact-name">{t('auth.name')}</label><input id="contact-name" name="name" required /></div><div className="field"><label htmlFor="contact-relationship">{t('contacts.relationship')}</label><input id="contact-relationship" name="relationship" required /></div><div className="field"><label htmlFor="contact-phone">{t('contacts.phone')}</label><input id="contact-phone" name="phone" type="tel" required /></div>{error && <p className="field-error" role="alert">{error}</p>}<div className="button-row"><button className="button" type="submit" disabled={pending}>{pending ? t('common.loading') : t('common.save')}</button><button className="button button--secondary" type="button" onClick={() => setAdding(false)}>{t('common.cancel')}</button></div></form>}
    </div>
  );
}

const shareOptions = [
  { field: 'name', key: 'sharing.identity' },
  { field: 'allergies', key: 'sharing.allergies' },
  { field: 'conditions', key: 'sharing.conditions' },
  { field: 'medicines', key: 'sharing.medicines' },
  { field: 'emergencyContact', key: 'sharing.contact' },
] as const;

export function PrivacyPage() {
  const { t } = useTranslation();
  const { profile, setProfile } = useAppState();
  const [draftFields, setDraftFields] = useState(profile.shareFields);
  const [notice, setNotice] = useState('');
  const save = useMutation({ mutationFn: () => api.updateProfile({ ...profile, shareFields: draftFields }), onSuccess: (saved) => { setProfile(saved); setNotice(t('common.done')); }, onError: () => setNotice(t('errors.genericBody')) });
  const toggle = (field: string, enabled: boolean) => setDraftFields((current) => enabled ? [...new Set([...current, field])] : current.filter((item) => item !== field));
  return <div><PageHeading title={t('sharing.title')} description={t('sharing.intro')} /><section className="card card--raised">{shareOptions.map((option) => <Toggle key={option.field} label={t(option.key)} checked={draftFields.includes(option.field)} onChange={(enabled) => toggle(option.field, enabled)} />)}<div className="privacy-lock section"><LockKeyhole aria-hidden="true" /><span>{t('sharing.hidden')}</span></div><button className="button" type="button" disabled={save.isPending} onClick={() => save.mutate()}>{save.isPending ? t('common.loading') : t('common.save')}</button><p role="status">{notice}</p></section></div>;
}

const onboardingSteps = ['onboarding.language', 'onboarding.details', 'onboarding.health', 'onboarding.people', 'onboarding.privacy', 'onboarding.review'] as const;

export function OnboardingPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { profile, setProfile, updatePreferences } = useAppState();
  const [draft, setDraft] = useState(profile);
  const [step, setStep] = useState(0);
  const [approved, setApproved] = useState(false);
  const [error, setError] = useState('');
  const update = (value: Partial<EmergencyProfile>) => setDraft((current) => ({ ...current, ...value }));
  const mutation = useMutation({ mutationFn: () => api.updateProfile({ ...draft, reviewedAt: new Date().toISOString() }), onSuccess: (saved) => { setProfile(saved); updatePreferences({ language: saved.preferredLanguage }); void navigate('/home'); }, onError: () => setError(t('errors.genericBody')) });

  const next = () => {
    if (step === 1 && (!draft.name || !draft.dateOfBirth)) { setError(t('auth.invalid')); return; }
    setError(''); setStep((current) => Math.min(current + 1, onboardingSteps.length - 1));
  };

  let content: ReactNode;
  if (step === 0) content = <div className="form-grid"><div className="field"><label htmlFor="preferred-language">{t('languages.current')}</label><select id="preferred-language" value={draft.preferredLanguage} onChange={(event) => update({ preferredLanguage: event.target.value })}>{supportedLanguages.map((language) => <option value={language.code} key={language.code}>{language.label}</option>)}</select></div><fieldset className="fieldset"><legend>{t('onboarding.response')}</legend><div className="choice-grid">{(['text', 'audio', 'both'] as const).map((mode) => <label className="choice-card" key={mode}><input type="radio" name="responseMode" checked={draft.responseMode === mode} onChange={() => update({ responseMode: mode })} /><span>{t(`onboarding.${mode}`)}</span></label>)}</div></fieldset></div>;
  else if (step === 1) content = <div className="form-grid"><div className="field"><label htmlFor="profile-name">{t('auth.name')}</label><input id="profile-name" value={draft.name} onChange={(event) => update({ name: event.target.value })} /></div><div className="field"><label htmlFor="profile-dob">{t('profile.dob')}</label><input id="profile-dob" type="date" value={draft.dateOfBirth} onChange={(event) => update({ dateOfBirth: event.target.value })} /></div><div className="field"><label htmlFor="profile-blood">{t('profile.blood')}</label><input id="profile-blood" value={draft.bloodGroup ?? ''} onChange={(event) => update({ bloodGroup: event.target.value })} /></div><div className="field"><label htmlFor="profile-hospital">{t('profile.hospital')}</label><input id="profile-hospital" value={draft.preferredHospital} onChange={(event) => update({ preferredHospital: event.target.value })} /></div><div className="field"><label htmlFor="profile-doctor">{t('profile.doctor')}</label><input id="profile-doctor" value={draft.doctor} onChange={(event) => update({ doctor: event.target.value })} /></div><div className="field"><label htmlFor="profile-insurance">{t('profile.insurance')}</label><input id="profile-insurance" value={draft.insurance} onChange={(event) => update({ insurance: event.target.value })} /></div></div>;
  else if (step === 2) content = <div className="form-grid"><p className="safety-note">{t('common.selfReported')}</p>{(['allergies', 'conditions', 'medicines', 'procedures'] as const).map((field) => <div className="field" key={field}><label htmlFor={`health-${field}`}>{t(`profile.${field}`)}</label><textarea id={`health-${field}`} value={draft[field].join('\n')} onChange={(event) => update({ [field]: event.target.value.split('\n').map((item) => item.trim()).filter(Boolean) })} /></div>)}</div>;
  else if (step === 3) content = <div className="stack"><p>{t('contacts.intro')}</p>{draft.contacts.map((contact) => <div className="card" key={contact.id}><strong>{contact.name}</strong><p>{contact.relationship} · {contact.phone}</p></div>)}<Link className="button button--secondary" to="/contacts">{t('contacts.add')}</Link></div>;
  else if (step === 4) content = <div className="stack">{shareOptions.map((option) => <Toggle key={option.field} label={t(option.key)} checked={draft.shareFields.includes(option.field)} onChange={(enabled) => update({ shareFields: enabled ? [...new Set([...draft.shareFields, option.field])] : draft.shareFields.filter((item) => item !== option.field) })} />)}<p className="privacy-lock"><LockKeyhole aria-hidden="true" />{t('sharing.hidden')}</p></div>;
  else content = <div className="stack"><ProfileSummary profile={draft} /><label className="choice-card"><input type="checkbox" checked={approved} onChange={(event) => setApproved(event.target.checked)} /><Check aria-hidden="true" /><span>{t('onboarding.approve')}</span></label></div>;

  return <div><PageHeading eyebrow={t('onboarding.step', { current: step + 1, total: onboardingSteps.length })} title={t('onboarding.title')} description={t(onboardingSteps[step] ?? 'onboarding.review')} /><div className="progress-steps" aria-hidden="true">{onboardingSteps.map((key, index) => <span className={`progress-step ${index <= step ? 'progress-step--complete' : ''}`} key={key} />)}</div><section className="card card--raised">{content}{error && <p className="field-error" role="alert">{error}</p>}<div className="button-row section">{step > 0 && <button className="button button--secondary" type="button" onClick={() => setStep((current) => current - 1)}>{t('common.back')}</button>}{step < onboardingSteps.length - 1 ? <button className="button" type="button" onClick={next}>{t('common.continue')}</button> : <button className="button" type="button" disabled={!approved || mutation.isPending} onClick={() => mutation.mutate()}>{mutation.isPending ? t('common.loading') : t('onboarding.finish')}</button>}</div></section></div>;
}

function ProfileSummary({ profile }: { profile: EmergencyProfile }) {
  const { t } = useTranslation();
  return <dl className="data-list"><div><dt>{t('auth.name')}</dt><dd>{profile.name}</dd></div><div><dt>{t('profile.dob')}</dt><dd>{profile.dateOfBirth}</dd></div><div><dt>{t('profile.allergies')}</dt><dd>{profile.allergies.join(', ')}</dd></div><div><dt>{t('contacts.title')}</dt><dd>{profile.contacts.length}</dd></div><div><dt>{t('sharing.title')}</dt><dd>{profile.shareFields.length}</dd></div></dl>;
}

export function ReadinessPage() {
  const { t } = useTranslation();
  const query = useQuery({ queryKey: ['readiness'], queryFn: () => api.getReadiness(), retry: 1 });
  if (!query.data) return <div role="status">{query.isError ? t('errors.genericBody') : t('common.loading')}</div>;
  const result = query.data;
  return <div><PageHeading title={t('readiness.title')} description={t('readiness.deterministic')} /><div className="two-column"><section className="card card--raised"><div className="score-ring" style={{ '--score': `${result.score}%` } as React.CSSProperties}><strong>{result.score}</strong></div><h2>{t('readiness.score', { score: result.score })}</h2></section><section className="stack"><article className="card"><h2>{t('readiness.complete')}</h2><ul className="plain-list">{result.completed.map((item) => <li key={item}><Check aria-hidden="true" /> {item}</li>)}</ul></article><article className="card"><h2>{t('readiness.improve')}</h2><ul className="plain-list">{result.improvements.map((item) => <li key={item}>{item}</li>)}</ul></article></section></div></div>;
}

export function HistoryPage() {
  const { t } = useTranslation();
  const sessions = useQuery({ queryKey: ['sessions-history'], queryFn: () => api.listSessions(), retry: 1 });
  if (sessions.isPending) return <div role="status">{t('common.loading')}</div>;
  return <div><PageHeading title={t('history.title')} description={t('history.intro')} />{sessions.data?.length ? <div className="card-grid">{sessions.data.map((session) => <article className="card card--raised" key={session.id}><div className="page-heading__row"><div><p className="eyebrow">{session.id}</p><h2>{session.patient || t('common.unknown')}</h2><p>{session.category.replaceAll('-', ' ')} · {session.location || t('common.unknown')}</p><span className="task-status">{t(`history.${session.status === 'closed' ? 'closed' : 'active'}`)}</span></div><Link className="button" to={`/emergency/${session.id}`}>{t('history.open')}<ArrowRight aria-hidden="true" /></Link></div></article>)}</div> : <p className="empty-state">{t('history.empty')}</p>}</div>;
}

function UserRoundPlaceholder() { return <span className="brand__mark" aria-hidden="true"><Accessibility /></span>; }

export function SettingsPage() {
  const { t } = useTranslation();
  const { preferences, updatePreferences, setAuthenticated } = useAppState();
  const navigate = useNavigate();
  const logout = async () => { await api.logout(); clearUserScopedOfflineData(); setAuthenticated(false); void navigate('/'); };
  return <div><PageHeading title={t('settings.title')} /><div className="two-column"><section className="card card--raised stack"><Link className="nav-link" to="/settings/language"><Languages aria-hidden="true" /><span>{t('settings.language')}</span><ArrowRight aria-hidden="true" /></Link><Link className="nav-link" to="/settings/accessibility"><Accessibility aria-hidden="true" /><span>{t('settings.accessibility')}</span><ArrowRight aria-hidden="true" /></Link></section><section className="card"><h2>{t('settings.appearance')}</h2><div className="choice-grid">{(['light', 'dark', 'system'] as const).map((theme) => <label className="choice-card" key={theme}><input type="radio" name="theme" checked={preferences.theme === theme} onChange={() => updatePreferences({ theme })} />{theme === 'dark' ? <Moon aria-hidden="true" /> : <Sun aria-hidden="true" />}<span>{t(`settings.${theme}`)}</span></label>)}</div><Toggle label={t('settings.simple')} hint={t('settings.simpleHint')} checked={preferences.simpleMode} onChange={(simpleMode) => updatePreferences({ simpleMode })} /><Toggle label={t('settings.offlineCard')} hint={t('settings.offlineHint')} checked={preferences.offlineCardEnabled} onChange={(offlineCardEnabled) => updatePreferences({ offlineCardEnabled })} /><button className="button button--ghost" type="button" onClick={() => void logout()}>{t('settings.signOut')}</button></section></div></div>;
}

export function LanguagePage() {
  const { t } = useTranslation();
  const { preferences, updatePreferences } = useAppState();
  return <div><PageHeading title={t('languages.title')} description={t('languages.note')} /><section className="card card--raised"><div className="field"><label htmlFor="language-setting">{t('languages.current')}</label><select id="language-setting" value={preferences.language} onChange={(event) => updatePreferences({ language: event.target.value })}>{supportedLanguages.map((language) => <option key={language.code} value={language.code}>{language.label}</option>)}</select></div></section></div>;
}

export function AccessibilityPage() {
  const { t } = useTranslation();
  const { preferences, updatePreferences } = useAppState();
  const toggle = <K extends keyof Preferences>(key: K) => (value: boolean) => updatePreferences({ [key]: value });
  return <div><PageHeading title={t('accessibility.title')} /><section className="card card--raised"><Toggle label={t('accessibility.highContrast')} checked={preferences.highContrast} onChange={toggle('highContrast')} /><Toggle label={t('accessibility.largeText')} checked={preferences.largeText} onChange={toggle('largeText')} /><Toggle label={t('accessibility.reducedMotion')} checked={preferences.reducedMotion} onChange={toggle('reducedMotion')} /><div className="privacy-lock section"><ShieldCheck aria-hidden="true" /><span>{t('accessibility.keyboard')} {t('accessibility.zoom')}</span></div></section></div>;
}
