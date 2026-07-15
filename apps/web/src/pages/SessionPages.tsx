import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { QRCodeSVG } from 'qrcode.react';
import { useTranslation } from 'react-i18next';
import { Link, Navigate, useNavigate, useParams } from 'react-router-dom';
import { AlertTriangle, Clipboard, LockKeyhole, MapPin, QrCode } from 'lucide-react';
import { ApiError, api, type ShareTokenResult } from '../api';
import { ConnectionBanner, PageHeading, SourceBadge, emergencyNumber } from '../components/AppShell';
import { queueNoncriticalUpdate } from '../offline';
import { useSessionConnection } from '../realtime';
import { useAppState } from '../state';
import type { EmergencyTask, TaskStatus, TriState } from '../types';

function formatTime(value: string, language: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat(language, { hour: 'numeric', minute: '2-digit' }).format(date);
}

function SessionTabs({ id, current }: { id: string; current: 'room' | 'tasks' | 'responder' | 'handover' | 'share' }) {
  const { t } = useTranslation();
  const tabs = [
    { id: 'room', to: `/emergency/${id}`, label: t('room.live') },
    { id: 'tasks', to: `/emergency/${id}/tasks`, label: t('room.tasks') },
    { id: 'responder', to: `/emergency/${id}/responder`, label: t('room.responder') },
    { id: 'handover', to: `/emergency/${id}/handover`, label: t('room.handover') },
    { id: 'share', to: `/emergency/${id}/share`, label: t('room.share') },
  ];
  return <nav className="mobile-tabs" aria-label={t('room.title')}>{tabs.map((tab) => <Link key={tab.id} to={tab.to} aria-current={tab.id === current ? 'page' : undefined}>{tab.label}</Link>)}</nav>;
}

function useAuthoritativeSession() {
  const { session, setSession, online } = useAppState();
  const { sessionId } = useParams();
  const id = sessionId ?? session.id;
  const query = useQuery({ queryKey: ['session', id], queryFn: () => api.getSession(id), initialData: session.id === id ? session : undefined, enabled: Boolean(id) && online, retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 1 });
  const connection = useSessionConnection(id, online, () => { void query.refetch(); });

  useEffect(() => {
    if (query.data) setSession(query.data);
  }, [query.data, setSession]);

  useEffect(() => {
    if (connection !== 'polling' || !online) return;
    const interval = window.setInterval(() => void query.refetch(), 8_000);
    return () => window.clearInterval(interval);
  }, [connection, online, query.refetch]);

  return { id, session: query.data, connection, refetch: query.refetch, error: query.error, mismatchOffline: !online && session.id !== id };
}

function SessionFailure({ error, mismatchOffline }: { error: Error | null; mismatchOffline: boolean }) {
  const { t } = useTranslation();
  if (error instanceof ApiError && (error.status === 401 || error.status === 403)) return <Navigate to="/unauthorized" replace />;
  if (!error && !mismatchOffline) return <div role="status">{t('common.loading')}</div>;
  return <section className="centered-state"><AlertTriangle aria-hidden="true" size={44} /><h1>{mismatchOffline ? t('offline.title') : t('errors.genericTitle')}</h1><p>{mismatchOffline ? t('offline.body', { number: emergencyNumber() }) : t('errors.genericBody')}</p><Link className="button" to="/home">{t('errors.home')}</Link></section>;
}

function ConfirmCloseDialog({ open, pending, onCancel, onConfirm }: { open: boolean; pending: boolean; onCancel(): void; onConfirm(): void }) {
  const { t } = useTranslation();
  const dialogRef = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    if (!open && dialog.open) dialog.close();
  }, [open]);
  return <dialog ref={dialogRef} className="dialog-card" aria-labelledby="close-title" aria-describedby="close-description" onCancel={(event) => { event.preventDefault(); onCancel(); }} onClose={onCancel}><h2 id="close-title">{t('room.closeConfirm')}</h2><p id="close-description">{t('room.closeWarning')}</p><div className="button-row"><button className="button button--secondary" type="button" autoFocus onClick={onCancel}>{t('common.cancel')}</button><button className="button button--danger" type="button" disabled={pending} onClick={onConfirm}>{pending ? t('common.loading') : t('room.close')}</button></div></dialog>;
}

export function CoordinationRoomPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { setSession, online } = useAppState();
  const { id, session, connection, error, mismatchOffline, refetch } = useAuthoritativeSession();
  const [observation, setObservation] = useState('');
  const [notice, setNotice] = useState('');
  const [showClose, setShowClose] = useState(false);
  const [closing, setClosing] = useState(false);
  const closeTriggerRef = useRef<HTMLButtonElement>(null);
  const language = i18n.resolvedLanguage ?? 'en';

  if (!session) return <SessionFailure error={error} mismatchOffline={mismatchOffline} />;

  const saveObservation = async (event: FormEvent) => {
    event.preventDefault();
    const message = observation.trim();
    if (!message) return;
    const timelineEvent = { id: crypto.randomUUID(), at: new Date().toISOString(), title: 'Observation added', detail: message, source: 'user-reported' as const, pending: !online };
    try {
      if (online) await api.addTimeline(id, 'observation', message);
      else queueNoncriticalUpdate({ sessionId: id, kind: 'observation', eventCode: 'noncritical-check-recorded' });
      setSession({ ...session, timeline: [...session.timeline, timelineEvent], updatedAt: timelineEvent.at });
      setObservation('');
      setNotice(online ? 'Observation saved.' : 'Observation saved on this device and waiting to sync.');
    } catch {
      setNotice(t('errors.genericBody'));
    }
  };

  const updateJourneyStatus = async (status: 'departed' | 'at-hospital') => {
    if (!online) {
      setNotice(t('status.offline'));
      return;
    }
    const type = status === 'departed' ? 'patient-departed' : 'patient-arrived';
    const message = status === 'departed' ? 'Patient departure reported by a participant.' : 'Hospital arrival reported by a participant.';
    try {
      await api.addTimeline(id, type, message);
      const event = { id: crypto.randomUUID(), at: new Date().toISOString(), title: status === 'departed' ? t('room.markDeparted') : t('room.markArrived'), detail: message, source: 'confirmed' as const };
      setSession({ ...session, status, timeline: [...session.timeline, event], updatedAt: event.at });
      setNotice(event.title);
    } catch {
      setNotice(t('errors.genericBody'));
    }
  };

  const closeSession = async () => {
    setClosing(true);
    try {
      await api.closeSession(id, session.concurrencyToken);
      setSession({ ...session, status: 'closed', updatedAt: new Date().toISOString() });
      navigate('/history');
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        await refetch();
        setNotice(t('errors.conflict', { defaultValue: 'This session changed elsewhere. The latest state is now shown; review it before closing.' }));
      } else setNotice(t('errors.genericBody'));
      setShowClose(false);
    } finally {
      setClosing(false);
    }
  };

  return (
    <div>
      <PageHeading eyebrow={session.id} title={t('room.title')} description={t('room.patient', { name: session.patient })} />
      <ConnectionBanner state={connection} />
      <SessionTabs id={id} current="room" />
      <div className="room-layout section">
        <div className="stack">
          <section className="card card--raised">
            <div className="section-heading"><h2>{t('room.live')}</h2><span className="task-status task-status--accepted">{session.status.replace('-', ' ')}</span></div>
            <dl className="metric-grid">
              <div className="metric"><dt>{t('room.location')}</dt><dd><MapPin aria-hidden="true" size={16} /> {session.location || t('common.unknown')}</dd></div>
              <div className="metric"><dt>{t('category.chestPain')}</dt><dd>{session.category.replaceAll('-', ' ')}</dd></div>
              <div className="metric"><dt>{t('brief.confidence')}</dt><dd>{Math.round(session.extraction.confidence * 100)}%</dd></div>
              <div className="metric"><dt>{t('brief.protocol')}</dt><dd>{session.protocolVersion}</dd></div>
            </dl>
            <div className="button-row section"><button className="button button--secondary" type="button" disabled={!online || session.status !== 'active'} onClick={() => void updateJourneyStatus('departed')}>{t('room.markDeparted')}</button><button className="button button--secondary" type="button" disabled={!online || session.status === 'closed'} onClick={() => void updateJourneyStatus('at-hospital')}>{t('room.markArrived')}</button></div>
          </section>
          <section className="card">
            <h2>{t('room.timeline')}</h2>
            <ol className="timeline">{session.timeline.map((event) => <li key={event.id}><time dateTime={event.at}>{formatTime(event.at, language)}{event.pending ? ` · ${t('status.offline')}` : ''}</time><h3>{event.title}</h3><p>{event.detail}</p><SourceBadge source={event.source} /></li>)}</ol>
          </section>
          <form className="card form-grid" onSubmit={(event) => void saveObservation(event)}>
            <div className="field"><label htmlFor="new-observation">{t('bystander.report')}</label><textarea id="new-observation" value={observation} onChange={(event) => setObservation(event.target.value)} maxLength={500} /></div>
            <button className="button" type="submit" disabled={!observation.trim()}>{t('common.save')}</button>
          </form>
        </div>
        <aside className="stack room-layout__side">
          <section className="card"><h2>{t('room.participants')}</h2><ul className="participant-list">{session.participants.map((participant) => <li key={participant}><span className="participant-dot" aria-hidden="true" />{participant}</li>)}</ul></section>
          <section className="card"><h2>{t('room.tasks')}</h2><ul className="task-list">{session.tasks.slice(0, 3).map((task) => <li key={task.id}><strong>{task.title}</strong><small>{task.assignee} · {task.status}</small></li>)}</ul><Link className="button button--secondary button--full" to={`/emergency/${id}/tasks`}>{t('tasks.title')}</Link></section>
          <button ref={closeTriggerRef} className="button button--ghost" type="button" onClick={() => setShowClose(true)}>{t('room.close')}</button>
        </aside>
      </div>
      <p role="status" aria-live="polite">{notice}</p>
      <ConfirmCloseDialog open={showClose} pending={closing} onCancel={() => { setShowClose(false); window.setTimeout(() => closeTriggerRef.current?.focus()); }} onConfirm={() => void closeSession()} />
    </div>
  );
}

function taskAction(status: TaskStatus): { next: TaskStatus; key: string }[] {
  if (status === 'open' || status === 'declined') return [{ next: 'accepted', key: 'tasks.accept' }, { next: 'declined', key: 'tasks.decline' }];
  if (status === 'accepted') return [{ next: 'completed', key: 'tasks.complete' }, { next: 'declined', key: 'tasks.decline' }];
  return [];
}

export function TaskBoardPage() {
  const { t, i18n } = useTranslation();
  const { setSession, online } = useAppState();
  const { id, session, connection, error: sessionError, mismatchOffline } = useAuthoritativeSession();
  const [error, setError] = useState('');
  const mutation = useMutation({
    mutationFn: ({ task, status, assignedParticipantId }: { task: EmergencyTask; status: TaskStatus | 'assigned'; assignedParticipantId?: string }) => api.updateTask(id, task.id, status, assignedParticipantId ?? task.assignedParticipantId, task.concurrencyToken),
    onSuccess: setSession,
    onError: () => setError(t('errors.genericBody')),
  });
  if (!session) return <SessionFailure error={sessionError} mismatchOffline={mismatchOffline} />;
  return (
    <div>
      <PageHeading eyebrow={session.id} title={t('tasks.title')} description={t('room.patient', { name: session.patient })} />
      <ConnectionBanner state={connection} />
      <SessionTabs id={id} current="tasks" />
      {!online && <p className="permission-note" role="status">{t('offline.body', { number: emergencyNumber() })}</p>}
      {error && <p className="field-error" role="alert">{error}</p>}
      <ul className="task-list section">{session.tasks.map((task) => <li className="task-card" key={task.id}><div className="task-card__top"><div>{task.critical && <span className="critical-pill"><AlertTriangle aria-hidden="true" />Critical</span>}<h3>{task.title}</h3><p>{t('tasks.owner', { name: task.assignee })}</p></div><span className={`task-status task-status--${task.status}`}>{t(`tasks.${task.status}`)}</span></div>{task.status !== 'accepted' && task.status !== 'completed' && <div className="field"><label htmlFor={`assignee-${task.id}`}>{t('tasks.assign')}</label><select id={`assignee-${task.id}`} value={task.assignedParticipantId ?? ''} disabled={!online || mutation.isPending} onChange={(event) => event.target.value && mutation.mutate({ task, status: 'assigned', assignedParticipantId: event.target.value })}><option value="">{t('common.unknown')}</option>{session.participantOptions?.map((participant) => <option key={participant.id} value={participant.id}>{participant.label}</option>)}</select></div>}<small>{t('tasks.updated', { time: formatTime(task.updatedAt, i18n.resolvedLanguage ?? 'en') })}</small><div className="button-row">{taskAction(task.status).map((action) => <button key={action.next} className={action.next === 'completed' ? 'button' : 'button button--secondary'} type="button" disabled={!online || mutation.isPending} onClick={() => mutation.mutate({ task, status: action.next })}>{t(action.key)}</button>)}</div></li>)}</ul>
    </div>
  );
}

function FactList({ values, source }: { values: string[]; source: string }) {
  return <ul className="plain-list">{values.map((value) => <li key={value}>{value} <SourceBadge source={source} /></li>)}</ul>;
}

export function ResponderBriefPage() {
  const { t } = useTranslation();
  const { profile } = useAppState();
  const { id, session, error, mismatchOffline } = useAuthoritativeSession();
  const [copied, setCopied] = useState(false);
  if (!session) return <SessionFailure error={error} mismatchOffline={mismatchOffline} />;
  const briefText = `${session.patient}\n${session.location}\n${session.extraction.observations.join('; ')}\nAllergies: ${profile.allergies.join(', ')}\nProtocol: ${session.protocolVersion}\nAI confidence: ${Math.round(session.extraction.confidence * 100)}%`;
  const copy = async () => { await navigator.clipboard.writeText(briefText); setCopied(true); };
  return (
    <div>
      <PageHeading eyebrow={session.id} title={t('brief.responderTitle')} description={t('brief.responderIntro')}><button className="button button--secondary" type="button" onClick={() => void copy()}><Clipboard aria-hidden="true" />{t('brief.copy')}</button></PageHeading>
      <SessionTabs id={id} current="responder" />
      <article className="card card--raised section">
        <section className="brief-section"><h2>{t('brief.identity')}</h2><dl className="data-list"><div><dt>{t('brief.identity')}</dt><dd>{profile.name} <SourceBadge source="profile" /></dd></div><div><dt>{t('room.location')}</dt><dd>{session.location} <SourceBadge source="user-reported" /></dd></div></dl></section>
        <section className="brief-section"><h2>{t('brief.observations')}</h2><FactList values={session.extraction.observations} source="ai-extracted" /><dl className="data-list"><div><dt>Conscious</dt><dd>{session.extraction.isConscious}</dd></div><div><dt>Breathing normally</dt><dd>{session.extraction.isBreathingNormally}</dd></div></dl></section>
        <section className="brief-section"><h2>{t('brief.allergies')}</h2><FactList values={profile.allergies} source="profile" /></section>
        <section className="brief-section"><h2>{t('brief.conditions')}</h2><FactList values={profile.conditions} source="profile" /></section>
        <section className="brief-section"><h2>{t('brief.medicines')}</h2><FactList values={profile.medicines} source="profile" /></section>
        <section className="brief-section"><h2>{t('brief.uncertainty')}</h2><FactList values={session.extraction.uncertainties} source="unknown" /><p>{t('brief.confidence')}: {Math.round(session.extraction.confidence * 100)}%</p></section>
      </article>
      <p className="disclaimer">{t('common.protocolDisclaimer')}</p><p className="sr-only" role="status" aria-live="polite">{copied ? t('brief.copied') : ''}</p>
    </div>
  );
}

export function HospitalHandoverPage() {
  const { t, i18n } = useTranslation();
  const { profile } = useAppState();
  const { id, session, error, mismatchOffline } = useAuthoritativeSession();
  if (!session) return <SessionFailure error={error} mismatchOffline={mismatchOffline} />;
  return (
    <div>
      <PageHeading eyebrow={session.id} title={t('brief.handoverTitle')} description={t('brief.handoverIntro')}><button className="button button--secondary" type="button" onClick={() => window.print()}>{t('common.save')}</button></PageHeading>
      <SessionTabs id={id} current="handover" />
      <article className="card card--raised section">
        <section className="brief-section"><h2>{t('brief.original')}</h2><p className="transcript" lang={/^hi(?:ndi)?$/i.test(session.extraction.detectedLanguage) ? 'hi' : undefined}>{session.originalInput}</p><SourceBadge source="user-reported" /></section>
        <section className="brief-section"><h2>{t('brief.observations')}</h2><FactList values={session.extraction.handoverFacts} source="ai-extracted" /></section>
        <section className="brief-section"><h2>{t('common.selfReported')}</h2><dl className="data-list"><div><dt>{t('brief.identity')}</dt><dd>{profile.name}</dd></div><div><dt>{t('brief.allergies')}</dt><dd>{profile.allergies.join(', ')}</dd></div><div><dt>{t('brief.conditions')}</dt><dd>{profile.conditions.join(', ')}</dd></div><div><dt>{t('brief.medicines')}</dt><dd>{profile.medicines.join(', ')}</dd></div><div><dt>{t('brief.procedure')}</dt><dd>{profile.procedures.join(', ')}</dd></div></dl></section>
        <section className="brief-section"><h2>{t('brief.timeline')}</h2><ol className="timeline">{session.timeline.map((event) => <li key={event.id}><time>{formatTime(event.at, i18n.resolvedLanguage ?? 'en')}</time><h3>{event.title}</h3><p>{event.detail}</p><SourceBadge source={event.source} /></li>)}</ol></section>
        <section className="brief-section"><h2>{t('brief.uncertainty')}</h2><FactList values={session.extraction.uncertainties} source="unknown" /><dl className="data-list"><div><dt>{t('brief.protocol')}</dt><dd>{session.protocolVersion}</dd></div><div><dt>{t('brief.confidence')}</dt><dd>{Math.round(session.extraction.confidence * 100)}%</dd></div><div><dt>Languages used</dt><dd>{session.extraction.detectedLanguage}, English normalization</dd></div></dl></section>
      </article>
      <p className="disclaimer">{t('common.protocolDisclaimer')}</p>
    </div>
  );
}

export function ShareQrPage() {
  const { t } = useTranslation();
  const { id, session, error: sessionError, mismatchOffline } = useAuthoritativeSession();
  const [share, setShare] = useState<ShareTokenResult | null>(null);
  const [error, setError] = useState('');
  const create = useMutation({ mutationFn: () => api.createShareToken(id), onSuccess: setShare, onError: () => setError(t('errors.genericBody')) });
  const revoke = useMutation({ mutationFn: () => share ? api.revokeShareToken(id, share.tokenId) : Promise.resolve(), onSuccess: () => setShare(null), onError: () => setError(t('errors.genericBody')) });
  if (!session) return <SessionFailure error={sessionError} mismatchOffline={mismatchOffline} />;
  const link = share ? `${window.location.origin}/share/${encodeURIComponent(share.token)}` : '';
  return (
    <div>
      <PageHeading eyebrow={session.id} title={t('room.share')} description={t('bystander.limited')} />
      <SessionTabs id={id} current="share" />
      <div className="two-column section">
        <section className="card card--raised stack">
          {share ? <><div className="qr-wrap" role="img" aria-label="Emergency sharing QR code"><QRCodeSVG value={link} size={220} level="M" title="Emergency sharing QR code" /><strong>{t('bystander.limited')}</strong></div><p>Expires {new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' }).format(new Date(share.expiresAtUtc))}</p><button className="button button--secondary" type="button" disabled={revoke.isPending} onClick={() => revoke.mutate()}>{t('sharing.revoke')}</button></> : <button className="button" type="button" disabled={create.isPending} onClick={() => create.mutate()}><QrCode aria-hidden="true" />{t('readiness.qr')}</button>}
          {error && <p className="field-error" role="alert">{error}</p>}
        </section>
        <aside className="card stack"><div className="privacy-lock"><LockKeyhole aria-hidden="true" /><span>{t('bystander.noRecords')}</span></div><h2>{t('sharing.title')}</h2><ul className="chip-list">{session.sharingFields.map((field) => <li className="chip" key={field}>{field}</li>)}</ul><p>{t('sharing.hidden')}</p></aside>
      </div>
    </div>
  );
}

export function BystanderPage() {
  const { t } = useTranslation();
  const { token = '' } = useParams();
  const [answers, setAnswers] = useState<{ conscious?: TriState; breathingNormally?: TriState; severeBleeding?: TriState }>({});
  const [notice, setNotice] = useState('');
  const query = useQuery({ queryKey: ['bystander', token], queryFn: () => api.getBystander(token), enabled: Boolean(token), retry: false });
  const report = useMutation({ mutationFn: () => api.reportBystanderObservation(token, answers), onSuccess: () => setNotice(t('common.done')), onError: () => setNotice(t('errors.genericBody')) });

  const shareLocation = () => {
    if (!navigator.geolocation) {
      setNotice(t('capture.locationDenied'));
      return;
    }
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => void api.reportBystanderLocation(token, coords.latitude, coords.longitude).then(() => setNotice(t('common.done'))).catch(() => setNotice(t('errors.genericBody'))),
      () => setNotice(t('capture.locationDenied')),
      { timeout: 8_000, enableHighAccuracy: false },
    );
  };

  if (query.isPending) return <section className="centered-state" role="status"><p>{t('common.loading')}</p></section>;
  if (query.isError || !query.data) return <section className="centered-state"><AlertTriangle aria-hidden="true" size={44} /><h1>{t('bystander.expired')}</h1><p>{t('bystander.noRecords')}</p></section>;
  const view = query.data;
  const number = emergencyNumber();
  const choices: { value: TriState; label: string }[] = [{ value: 'yes', label: t('common.yes') }, { value: 'no', label: t('common.no') }, { value: 'unknown', label: t('common.unknown') }];
  const fields = [
    { id: 'conscious', key: 'bystander.conscious' as const },
    { id: 'breathingNormally', key: 'bystander.breathing' as const },
    { id: 'severeBleeding', key: 'bystander.bleeding' as const },
  ];

  return (
    <div>
      <PageHeading eyebrow={t('bystander.limited')} title={t('bystander.title')} description={`${view.patientName} · ${view.approximateAge}`} />
      <a className="button button--danger button--full" href={`tel:${number}`}>{t('bystander.call', { number })}</a>
      <div className="two-column section">
        <section className="card card--raised"><h2>{view.patientName ?? t('common.unknown')}</h2><dl className="data-list"><div><dt>{t('room.location')}</dt><dd>{view.location || t('common.unknown')}</dd></div><div><dt>{t('brief.allergies')}</dt><dd>{view.allergies.join(', ') || t('common.unknown')}</dd></div><div><dt>{t('brief.conditions')}</dt><dd>{view.conditions.join(', ') || t('common.unknown')}</dd></div><div><dt>{t('brief.medicines')}</dt><dd>{view.medicines.join(', ') || t('common.unknown')}</dd></div>{view.emergencyContact && <div><dt>{t('brief.contact')}</dt><dd>{view.emergencyContact.name} · {view.emergencyContact.relationship} · <a href={`tel:${view.emergencyContact.phone.replace(/[^+0-9]/g, '')}`}>{view.emergencyContact.phone}</a></dd></div>}</dl></section>
        <form className="card stack" onSubmit={(event) => { event.preventDefault(); report.mutate(); }}><h2>{t('bystander.report')}</h2>{fields.map((field) => <fieldset className="fieldset" key={field.id}><legend>{t(field.key)}</legend><div className="answer-grid">{choices.map((choice) => <button key={choice.value} className="answer-button" type="button" aria-pressed={answers[field.id as keyof typeof answers] === choice.value} onClick={() => setAnswers((current) => ({ ...current, [field.id]: choice.value }))}>{choice.label}</button>)}</div></fieldset>)}<button className="button" type="submit" disabled={Object.keys(answers).length === 0 || report.isPending}>{t('common.save')}</button><button className="button button--secondary" type="button" onClick={shareLocation}><MapPin aria-hidden="true" />{t('bystander.location')}</button><p className="field-hint">{t('bystander.consent')}</p></form>
      </div>
      <div className="privacy-lock"><LockKeyhole aria-hidden="true" /><span>{t('bystander.noRecords')}</span></div><p className="disclaimer">{t('common.protocolDisclaimer')}</p><p role="status" aria-live="polite">{notice}</p>
    </div>
  );
}
