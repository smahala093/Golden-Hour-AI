import { useCallback, useEffect, useLayoutEffect, useRef, useState, type FormEvent } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { QRCodeSVG } from 'qrcode.react';
import { useTranslation } from 'react-i18next';
import { Link, Navigate, useNavigate, useParams } from 'react-router-dom';
import { AlertTriangle, Clipboard, LockKeyhole, MapPin, QrCode, UserPlus } from 'lucide-react';
import { ApiError, api, type EmergencySummaryKind, type ParticipantInviteResult, type ShareTokenResult } from '../api';
import { ConnectionBanner, PageHeading, SourceBadge, emergencyNumber } from '../components/AppShell';
import { queueNoncriticalUpdate } from '../offline';
import { captureAndScrubParticipantInvite, hasPendingParticipantInvite, submitPendingParticipantInvite } from '../participantInvite';
import { useSessionConnection } from '../realtime';
import { useAppState } from '../state';
import type { EmergencyParticipant, EmergencyTask, ParticipantRole, PatientSnapshot, SessionObservation, TaskStatus, TriState } from '../types';
import type { IncidentCategory } from '../types';
import { loadCachedProtocol } from './EmergencyFlowPages';

function formatTime(value: string, language: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat(language, { hour: 'numeric', minute: '2-digit' }).format(date);
}

const participantRoleKeys: Record<ParticipantRole, 'room.roleOwner' | 'room.roleFamily' | 'room.roleBystander' | 'room.roleCaregiver'> = {
  owner: 'room.roleOwner', family: 'room.roleFamily', bystander: 'room.roleBystander', caregiver: 'room.roleCaregiver',
};

function SessionTabs({ id, current }: { id: string; current: 'room' | 'tasks' | 'family' | 'responder' | 'handover' | 'share' }) {
  const { t } = useTranslation();
  const tabs = [
    { id: 'room', to: `/emergency/${id}`, label: t('room.live') },
    { id: 'tasks', to: `/emergency/${id}/tasks`, label: t('room.tasks') },
    { id: 'family', to: `/emergency/${id}/family-summary`, label: t('summary.familyTab') },
    { id: 'responder', to: `/emergency/${id}/responder`, label: t('room.responder') },
    { id: 'handover', to: `/emergency/${id}/handover`, label: t('room.handover') },
    { id: 'share', to: `/emergency/${id}/share`, label: t('room.share') },
  ];
  return <nav className="mobile-tabs" aria-label={t('room.title')}>{tabs.map((tab) => <Link key={tab.id} to={tab.to} aria-current={tab.id === current ? 'page' : undefined}>{tab.label}</Link>)}</nav>;
}

export function ServerSummaryCard({ sessionId, kind }: { sessionId: string; kind: EmergencySummaryKind }) {
  const { t } = useTranslation();
  const [notice, setNotice] = useState('');
  const summary = useMutation({ mutationFn: () => api.generateSummary(sessionId, kind), onError: () => setNotice(t('errors.genericBody')) });
  const copy = async () => {
    if (!summary.data) return;
    try { await navigator.clipboard.writeText(summary.data.content); setNotice(t('summary.copied')); }
    catch { setNotice(t('errors.genericBody')); }
  };
  return <section className="card card--raised section" aria-labelledby={`server-summary-${kind}`}><h2 id={`server-summary-${kind}`}>{t(`summary.${kind === 'hospital-handover' ? 'handover' : kind}Title`)}</h2><p>{t('summary.serverNotice')}</p>{summary.data ? <><pre className="transcript summary-content">{summary.data.content}</pre><dl className="data-list"><div><dt>{t('brief.language')}</dt><dd>{summary.data.language}</dd></div><div><dt>{t('brief.protocol')}</dt><dd>{summary.data.protocolVersion}</dd></div></dl><button className="button button--secondary" type="button" onClick={() => void copy()}><Clipboard aria-hidden="true" />{t('brief.copy')}</button></> : <button className="button" type="button" disabled={summary.isPending} onClick={() => { setNotice(''); summary.mutate(); }}>{summary.isPending ? t('common.loading') : t('summary.generate')}</button>}<p role="status" aria-live="polite">{notice}</p></section>;
}

function useAuthoritativeSession() {
  const { session, setSession, online } = useAppState();
  const { sessionId } = useParams();
  const id = sessionId ?? session.id;
  const query = useQuery({ queryKey: ['session', id], queryFn: () => api.getSession(id), initialData: session.id === id ? session : undefined, enabled: Boolean(id) && online, retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 1 });
  const refetch = useCallback(() => query.refetch(), [query]);
  const connection = useSessionConnection(id, online, () => { void refetch(); });

  useEffect(() => {
    if (query.data) setSession(query.data);
  }, [query.data, setSession]);

  useEffect(() => {
    if (connection !== 'polling' || !online) return;
    const interval = window.setInterval(() => void refetch(), 8_000);
    return () => window.clearInterval(interval);
  }, [connection, online, refetch]);

  return { id, session: query.data, connection, refetch, error: query.error, mismatchOffline: !online && session.id !== id };
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

function InviteParticipantCard({ sessionId, online, onCreated }: { sessionId: string; online: boolean; onCreated(): void }) {
  const { t, i18n } = useTranslation();
  const [displayName, setDisplayName] = useState('');
  const [role, setRole] = useState<Exclude<ParticipantRole, 'owner'>>('family');
  const [invitation, setInvitation] = useState<ParticipantInviteResult | null>(null);
  const [notice, setNotice] = useState('');
  const mutation = useMutation({
    mutationFn: () => api.inviteParticipant(sessionId, displayName.trim(), role),
    onSuccess: (result) => {
      setInvitation(result);
      setDisplayName('');
      setNotice(t('room.inviteReady'));
      onCreated();
    },
    onError: () => setNotice(t('errors.genericBody')),
  });
  const inviteUrl = invitation && invitation.path.startsWith(`/emergency/${sessionId}/join#`) ? new URL(invitation.path, window.location.origin).toString() : '';
  const copyInvitation = async () => {
    if (!inviteUrl) return;
    await navigator.clipboard.writeText(inviteUrl);
    setNotice(t('room.copiedInvite'));
  };
  return (
    <section className="card stack">
      <h2>{t('room.invite')}</h2>
      <form className="form-grid" onSubmit={(event) => { event.preventDefault(); if (displayName.trim()) mutation.mutate(); }}>
        <div className="field"><label htmlFor="participant-name">{t('room.inviteName')}</label><input id="participant-name" value={displayName} onChange={(event) => setDisplayName(event.target.value)} maxLength={120} required /></div>
        <div className="field"><label htmlFor="participant-role">{t('room.inviteRole')}</label><select id="participant-role" value={role} onChange={(event) => setRole(event.target.value as Exclude<ParticipantRole, 'owner'>)}><option value="family">{t('room.roleFamily')}</option><option value="caregiver">{t('room.roleCaregiver')}</option><option value="bystander">{t('room.roleBystander')}</option></select></div>
        <button className="button button--secondary button--full" type="submit" disabled={!online || !displayName.trim() || mutation.isPending}><UserPlus aria-hidden="true" />{mutation.isPending ? t('common.loading') : t('room.inviteSubmit')}</button>
      </form>
      {inviteUrl && invitation && <div className="qr-wrap"><QRCodeSVG value={inviteUrl} size={180} level="M" title={t('room.inviteReady')} /><strong>{t('room.inviteReady')}</strong><p>{t('sharing.expires', { time: formatTime(invitation.expiresAtUtc, i18n.resolvedLanguage ?? 'en') })}</p><button className="button button--secondary" type="button" onClick={() => void copyInvitation()}><Clipboard aria-hidden="true" />{t('room.copyInvite')}</button></div>}
      <p role="status" aria-live="polite">{notice}</p>
    </section>
  );
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
    const timelineEvent = { id: crypto.randomUUID(), at: new Date().toISOString(), title: t('room.observationAdded'), detail: message, source: 'user-reported' as const, pending: !online };
    try {
      if (online) await api.addTimeline(id, 'observation', message);
      else queueNoncriticalUpdate({ sessionId: id, kind: 'observation', eventCode: 'noncritical-check-recorded' });
      setSession({ ...session, timeline: [...session.timeline, timelineEvent], updatedAt: timelineEvent.at });
      setObservation('');
      setNotice(online ? t('room.observationSaved') : t('room.observationOffline'));
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
    const message = status === 'departed' ? t('room.departureDetail') : t('room.arrivalDetail');
    try {
      await api.addTimeline(id, type, message);
      const event = { id: crypto.randomUUID(), at: new Date().toISOString(), title: status === 'departed' ? t('room.markDeparted') : t('room.markArrived'), detail: message, source: 'user-reported' as const };
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
      void navigate('/history');
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
      <PageHeading eyebrow={session.id} title={t('room.title')} description={t('room.patient', { name: session.patient || t('common.unknown') })} />
      <ConnectionBanner state={connection} />
      <SessionTabs id={id} current="room" />
      <div className="room-layout section">
        <div className="stack">
          <section className="card card--raised">
            <div className="section-heading"><h2>{t('room.live')}</h2><span className="task-status task-status--accepted">{t(({ active: 'room.statusActive', departed: 'room.statusDeparted', 'at-hospital': 'room.statusAtHospital', closed: 'room.statusClosed' } as const)[session.status])}</span></div>
            <dl className="metric-grid">
              <div className="metric"><dt>{t('room.location')}</dt><dd><MapPin aria-hidden="true" size={16} /> {session.location || t('common.unknown')}</dd></div>
              <div className="metric"><dt>{t('start.category')}</dt><dd>{t(`category.${({ 'chest-pain': 'chestPain', 'breathing-difficulty': 'breathingDifficulty', 'fall-injury': 'fallInjury', unconscious: 'unconscious', seizure: 'seizure', 'heavy-bleeding': 'heavyBleeding', 'road-accident': 'roadAccident', 'allergic-reaction': 'allergicReaction', 'child-emergency': 'childEmergency', unknown: 'unknown' } as const)[session.category]}`)}</dd></div>
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
          <section className="card"><h2>{t('room.participants')}</h2>{session.participantDetails.length > 0 ? <ul className="participant-list">{session.participantDetails.map((participant) => <li key={participant.id}><span className="participant-dot" aria-hidden="true" /><span><strong>{participant.displayName}</strong><small>{t(participantRoleKeys[participant.role])} · {participant.acknowledgedAtUtc ? t('room.acknowledged') : t('room.awaiting')}</small></span></li>)}</ul> : <p>{t('room.participantEmpty')}</p>}</section>
          <InviteParticipantCard sessionId={id} online={online} onCreated={() => { void refetch(); }} />
          <section className="card"><h2>{t('room.tasks')}</h2><ul className="task-list">{session.tasks.slice(0, 3).map((task) => <li key={task.id}><strong>{task.title}</strong><small>{task.assignee || t('common.unknown')} · {t(`tasks.${task.status}`)}</small></li>)}</ul><Link className="button button--secondary button--full" to={`/emergency/${id}/tasks`}>{t('tasks.title')}</Link></section>
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
      <PageHeading eyebrow={session.id} title={t('tasks.title')} description={t('room.patient', { name: session.patient || t('common.unknown') })} />
      <ConnectionBanner state={connection} />
      <SessionTabs id={id} current="tasks" />
      {!online && <p className="permission-note" role="status">{t('offline.body', { number: emergencyNumber() })}</p>}
      {error && <p className="field-error" role="alert">{error}</p>}
      <ul className="task-list section">{session.tasks.map((task) => <li className="task-card" key={task.id}><div className="task-card__top"><div>{task.critical && <span className="critical-pill"><AlertTriangle aria-hidden="true" />{t('tasks.critical')}</span>}<h3>{task.title}</h3><p>{t('tasks.owner', { name: task.assignee || t('common.unknown') })}</p></div><span className={`task-status task-status--${task.status}`}>{t(`tasks.${task.status}`)}</span></div>{task.status !== 'accepted' && task.status !== 'completed' && <div className="field"><label htmlFor={`assignee-${task.id}`}>{t('tasks.assign')}</label><select id={`assignee-${task.id}`} value={task.assignedParticipantId ?? ''} disabled={!online || mutation.isPending} onChange={(event) => event.target.value && mutation.mutate({ task, status: 'assigned', assignedParticipantId: event.target.value })}><option value="">{t('common.unknown')}</option>{session.participantOptions?.map((participant) => <option key={participant.id} value={participant.id}>{participant.label}</option>)}</select></div>}<small>{t('tasks.updated', { time: formatTime(task.updatedAt, i18n.resolvedLanguage ?? 'en') })}</small><div className="button-row">{taskAction(task.status).map((action) => <button key={action.next} className={action.next === 'completed' ? 'button' : 'button button--secondary'} type="button" disabled={!online || mutation.isPending} onClick={() => mutation.mutate({ task, status: action.next })}>{t(action.key)}</button>)}</div></li>)}</ul>
    </div>
  );
}

function FactList({ values, source }: { values: string[]; source: string }) {
  const { t } = useTranslation();
  return values.length > 0 ? <ul className="plain-list">{values.map((value) => <li key={value}>{value} <SourceBadge source={source} /></li>)}</ul> : <p>{t('common.unknown')}</p>;
}

function PatientSnapshotDetails({ snapshot }: { snapshot: PatientSnapshot | null }) {
  const { t, i18n } = useTranslation();
  if (!snapshot) return <section className="brief-section"><h2>{t('common.selfReported')}</h2><p>{t('brief.noSnapshot')}</p></section>;
  const procedures = snapshot.procedures.map((procedure) => `${procedure.name}${procedure.year ? `, ${procedure.year}` : ''}`);
  return (
    <section className="brief-section">
      <h2>{t('common.selfReported')} <SourceBadge source="profile" /></h2>
      <p className="field-hint">{t('brief.captured', { time: formatTime(snapshot.capturedAtUtc, i18n.resolvedLanguage ?? 'en') })}</p>
      <dl className="data-list">
        <div><dt>{t('brief.identity')}</dt><dd>{snapshot.fullName || t('common.unknown')}</dd></div>
        <div><dt>{t('offline.age')}</dt><dd>{snapshot.approximateAge ?? t('common.unknown')}</dd></div>
        <div><dt>{t('brief.allergies')}</dt><dd>{snapshot.allergies.join(', ') || t('common.unknown')}</dd></div>
        <div><dt>{t('brief.conditions')}</dt><dd>{snapshot.conditions.join(', ') || t('common.unknown')}</dd></div>
        <div><dt>{t('brief.medicines')}</dt><dd>{snapshot.medications.join(', ') || t('common.unknown')}</dd></div>
        <div><dt>{t('brief.procedure')}</dt><dd>{procedures.join(', ') || t('common.unknown')}</dd></div>
        {snapshot.emergencyContact && <div><dt>{t('brief.contact')}</dt><dd>{snapshot.emergencyContact.name} · {snapshot.emergencyContact.relationship} · <a href={`tel:${snapshot.emergencyContact.phoneNumber.replace(/[^+0-9]/g, '')}`}>{snapshot.emergencyContact.phoneNumber}</a></dd></div>}
      </dl>
    </section>
  );
}

function ObservationFacts({ observations }: { observations: SessionObservation[] }) {
  const { t } = useTranslation();
  if (observations.length === 0) return <p>{t('common.unknown')}</p>;
  const kindLabel = (kind: string) => ({ conscious: t('brief.conscious'), breathingnormally: t('brief.breathing'), severebleeding: t('brief.bleeding') } as Record<string, string>)[kind.replaceAll(/[^a-z]/gi, '').toLowerCase()] ?? kind.replaceAll('_', ' ');
  const valueLabel = (value: string) => ({ yes: t('common.yes'), no: t('common.no'), unknown: t('common.unknown') } as Record<string, string>)[value.toLowerCase()] ?? value;
  return <ul className="plain-list">{observations.map((observation) => <li key={observation.id}><strong>{kindLabel(observation.kind)}:</strong> {valueLabel(observation.value)} <SourceBadge source={observation.isConfirmed ? 'confirmed' : observation.source.toLowerCase().includes('ai') ? 'ai-extracted' : 'user-reported'} /></li>)}</ul>;
}

export function ResponderBriefPage() {
  const { t } = useTranslation();
  const { id, session, error, mismatchOffline } = useAuthoritativeSession();
  if (!session) return <SessionFailure error={error} mismatchOffline={mismatchOffline} />;
  const snapshot = session.patientSnapshot;
  const confirmedObservations = session.observations.filter((observation) => observation.isConfirmed);
  return (
    <div>
      <PageHeading eyebrow={session.id} title={t('brief.responderTitle')} description={t('brief.responderIntro')} />
      <SessionTabs id={id} current="responder" />
      <ServerSummaryCard sessionId={id} kind="responder" />
      <article className="card card--raised section">
        <section className="brief-section"><h2>{t('brief.identity')}</h2><dl className="data-list"><div><dt>{t('brief.identity')}</dt><dd>{snapshot?.fullName || t('common.unknown')} {snapshot && <SourceBadge source="profile" />}</dd></div><div><dt>{t('room.location')}</dt><dd>{session.location || t('common.unknown')} <SourceBadge source="user-reported" /></dd></div></dl></section>
        <section className="brief-section"><h2>{t('brief.confirmed')}</h2><ObservationFacts observations={confirmedObservations} /></section>
        <section className="brief-section"><h2>{t('brief.unconfirmed')}</h2><FactList values={session.extraction.observations} source="ai-extracted" /><dl className="data-list"><div><dt>{t('brief.conscious')}</dt><dd>{session.extraction.isConscious}</dd></div><div><dt>{t('brief.breathing')}</dt><dd>{session.extraction.isBreathingNormally}</dd></div></dl></section>
        <PatientSnapshotDetails snapshot={snapshot} />
        <section className="brief-section"><h2>{t('brief.uncertainty')}</h2><FactList values={session.extraction.uncertainties} source="unknown" /><p>{t('brief.confidence')}: {Math.round(session.extraction.confidence * 100)}%</p></section>
      </article>
      <p className="disclaimer">{t('common.protocolDisclaimer')}</p>
    </div>
  );
}

export function HospitalHandoverPage() {
  const { t, i18n } = useTranslation();
  const { id, session, error, mismatchOffline } = useAuthoritativeSession();
  if (!session) return <SessionFailure error={error} mismatchOffline={mismatchOffline} />;
  const confirmedObservations = session.observations.filter((observation) => observation.isConfirmed);
  const unconfirmedObservations = session.observations.filter((observation) => !observation.isConfirmed);
  const missingFacts = [...session.extraction.uncertainties, ...session.extraction.criticalMissingQuestions.map((question) => question.question)];
  return (
    <div>
      <PageHeading eyebrow={session.id} title={t('brief.handoverTitle')} description={t('brief.handoverIntro')}><button className="button button--secondary" type="button" onClick={() => window.print()}>{t('common.save')}</button></PageHeading>
      <SessionTabs id={id} current="handover" />
      <ServerSummaryCard sessionId={id} kind="hospital-handover" />
      <article className="card card--raised section">
        <section className="brief-section"><h2>{t('brief.original')}</h2><p className="transcript" lang={/^hi(?:ndi)?$/i.test(session.extraction.detectedLanguage) ? 'hi' : undefined}>{session.originalInput}</p><SourceBadge source="user-reported" /></section>
        <section className="brief-section"><h2>{t('brief.confirmed')}</h2><ObservationFacts observations={confirmedObservations} /></section>
        <section className="brief-section"><h2>{t('brief.unconfirmed')}</h2><ObservationFacts observations={unconfirmedObservations} /><FactList values={session.extraction.handoverFacts} source="ai-extracted" /></section>
        <PatientSnapshotDetails snapshot={session.patientSnapshot} />
        <section className="brief-section"><h2>{t('brief.timeline')}</h2><ol className="timeline">{session.timeline.map((event) => <li key={event.id}><time>{formatTime(event.at, i18n.resolvedLanguage ?? 'en')}</time><h3>{event.title}</h3><p>{event.detail}</p><SourceBadge source={event.source} /></li>)}</ol></section>
        <section className="brief-section"><h2>{t('brief.uncertainty')}</h2><FactList values={missingFacts} source="unknown" /><dl className="data-list"><div><dt>{t('brief.protocol')}</dt><dd>{session.protocolVersion || t('common.unknown')}</dd></div><div><dt>{t('brief.confidence')}</dt><dd>{Math.round(session.extraction.confidence * 100)}%</dd></div><div><dt>{t('brief.language')}</dt><dd>{t('brief.languageNormalization', { language: session.extraction.detectedLanguage })}</dd></div></dl></section>
      </article>
      <p className="disclaimer">{t('common.protocolDisclaimer')}</p>
    </div>
  );
}

export function FamilySummaryPage() {
  const { t } = useTranslation();
  const { id, session, error, mismatchOffline } = useAuthoritativeSession();
  if (!session) return <SessionFailure error={error} mismatchOffline={mismatchOffline} />;
  return <div><PageHeading eyebrow={session.id} title={t('summary.familyTitle')} description={t('summary.familyIntro')} /><SessionTabs id={id} current="family" /><ServerSummaryCard sessionId={id} kind="family" /><p className="disclaimer">{t('common.protocolDisclaimer')}</p></div>;
}

export function ShareQrPage() {
  const { t } = useTranslation();
  const { id, session, error: sessionError, mismatchOffline } = useAuthoritativeSession();
  const [share, setShare] = useState<ShareTokenResult | null>(null);
  const [error, setError] = useState('');
  const create = useMutation({ mutationFn: () => api.createShareToken(id), onSuccess: setShare, onError: () => setError(t('errors.genericBody')) });
  const revoke = useMutation({ mutationFn: () => share ? api.revokeShareToken(id, share.tokenId) : Promise.resolve(), onSuccess: () => setShare(null), onError: () => setError(t('errors.genericBody')) });
  if (!session) return <SessionFailure error={sessionError} mismatchOffline={mismatchOffline} />;
  const link = share && share.path.startsWith('/share#') ? new URL(share.path, window.location.origin).toString() : '';
  return (
    <div>
      <PageHeading eyebrow={session.id} title={t('room.share')} description={t('bystander.limited')} />
      <SessionTabs id={id} current="share" />
      <div className="two-column section">
        <section className="card card--raised stack">
          {share && link ? <><div className="qr-wrap" role="img" aria-label={t('sharing.qrAlt')}><QRCodeSVG value={link} size={220} level="M" title={t('sharing.qrAlt')} /><strong>{t('bystander.limited')}</strong></div><p>{t('sharing.expires', { time: new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' }).format(new Date(share.expiresAtUtc)) })}</p><button className="button button--secondary" type="button" disabled={revoke.isPending} onClick={() => revoke.mutate()}>{t('sharing.revoke')}</button></> : <button className="button" type="button" disabled={create.isPending} onClick={() => create.mutate()}><QrCode aria-hidden="true" />{t('readiness.qr')}</button>}
          {error && <p className="field-error" role="alert">{error}</p>}
        </section>
        <aside className="card stack"><div className="privacy-lock"><LockKeyhole aria-hidden="true" /><span>{t('bystander.noRecords')}</span></div><h2>{t('sharing.title')}</h2><ul className="chip-list">{session.sharingFields.map((field) => <li className="chip" key={field}>{t(({ name: 'sharing.identity', approximateAge: 'sharing.identity', allergies: 'sharing.allergies', conditions: 'sharing.conditions', medicines: 'sharing.medicines', emergencyContact: 'sharing.contact' } as Record<string, string>)[field] ?? field)}</li>)}</ul><p>{t('sharing.hidden')}</p></aside>
      </div>
    </div>
  );
}

export function JoinParticipantPage() {
  const { t } = useTranslation();
  const { sessionId = '' } = useParams();
  const navigate = useNavigate();
  const { setSession } = useAppState();
  const [fragmentScrubbed, setFragmentScrubbed] = useState(() => !window.location.hash);
  const [invitationAvailable, setInvitationAvailable] = useState(() => hasPendingParticipantInvite(sessionId));
  const [participant, setParticipant] = useState<EmergencyParticipant | null>(null);
  const [error, setError] = useState('');

  useLayoutEffect(() => {
    if (window.location.hash) captureAndScrubParticipantInvite(sessionId);
    setInvitationAvailable(hasPendingParticipantInvite(sessionId));
    setFragmentScrubbed(true);
  }, [sessionId]);

  const join = useMutation({
    mutationFn: () => submitPendingParticipantInvite(sessionId, (token) => api.joinParticipant(sessionId, token)),
    onSuccess: (joinedParticipant) => {
      setParticipant(joinedParticipant);
      setInvitationAvailable(false);
      setError('');
    },
    onError: () => setError(t('join.error')),
  });
  const acknowledge = useMutation({
    mutationFn: () => participant ? api.acknowledgeParticipant(sessionId, participant.id) : Promise.reject(new Error('Participant invitation has not been joined.')),
    onSuccess: () => {
      void api.getSession(sessionId).then(setSession).catch(() => undefined).finally(() => { void navigate(`/emergency/${sessionId}`, { replace: true }); });
    },
    onError: () => setError(t('errors.genericBody')),
  });

  if (!fragmentScrubbed) return <section className="centered-state" role="status"><p>{t('common.loading')}</p></section>;
  if (!invitationAvailable && !participant) return <section className="centered-state"><AlertTriangle aria-hidden="true" size={44} /><h1>{t('join.title')}</h1><p>{t('join.missing')}</p><Link className="button" to="/home">{t('errors.home')}</Link></section>;
  return (
    <section className="card card--raised stack auth-card">
      <PageHeading title={t('join.title')} description={t('join.intro')} />
      {participant ? <><p>{t('join.accepted', { name: participant.displayName, role: t(participantRoleKeys[participant.role]) })}</p><button className="button" type="button" disabled={acknowledge.isPending} onClick={() => acknowledge.mutate()}>{acknowledge.isPending ? t('common.loading') : t('join.acknowledge')}</button></> : <button className="button" type="button" disabled={join.isPending} onClick={() => join.mutate()}>{join.isPending ? t('common.loading') : t('join.accept')}</button>}
      {error && <p className="field-error" role="alert">{error}</p>}
      <p className="disclaimer">{t('landing.safety')}</p>
    </section>
  );
}

export function BystanderPage() {
  const { t, i18n } = useTranslation();
  const [token] = useState(() => {
    const raw = window.location.hash.slice(1);
    if (!raw || raw.length > 1_024) return '';
    try { return decodeURIComponent(raw); } catch { return ''; }
  });
  const [answers, setAnswers] = useState<{ conscious?: TriState; breathingNormally?: TriState; severeBleeding?: TriState }>({});
  const [notice, setNotice] = useState('');
  const [fragmentScrubbed, setFragmentScrubbed] = useState(() => !window.location.hash);
  useLayoutEffect(() => {
    if (window.location.hash) window.history.replaceState(window.history.state, document.title, `${window.location.pathname}${window.location.search}`);
    setFragmentScrubbed(true);
  }, []);
  const query = useQuery({ queryKey: ['bystander-share'], queryFn: () => api.getBystander(token), enabled: Boolean(token) && fragmentScrubbed, retry: false, gcTime: 0 });
  const view = query.data;
  const language = (i18n.resolvedLanguage ?? i18n.language).split('-')[0] ?? 'en';
  const protocolQuery = useQuery({ queryKey: ['bystander-protocol', view?.category], queryFn: () => loadCachedProtocol((view?.category ?? 'unknown') as IncidentCategory), enabled: Boolean(view) && !view?.protocol, staleTime: Infinity });
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

  if (!token) return <section className="centered-state"><AlertTriangle aria-hidden="true" size={44} /><h1>{t('bystander.expired')}</h1><p>{t('bystander.noRecords')}</p></section>;
  if (query.isPending) return <section className="centered-state" role="status"><p>{t('common.loading')}</p></section>;
  if (query.isError || !query.data) return <section className="centered-state"><AlertTriangle aria-hidden="true" size={44} /><h1>{t('bystander.expired')}</h1><p>{t('bystander.noRecords')}</p></section>;
  const resolvedView = query.data;
  const protocol = resolvedView.protocol ?? protocolQuery.data;
  const number = resolvedView.emergencyNumber || emergencyNumber();
  const choices: { value: TriState; label: string }[] = [{ value: 'yes', label: t('common.yes') }, { value: 'no', label: t('common.no') }, { value: 'unknown', label: t('common.unknown') }];
  const fields = [
    { id: 'conscious', key: 'bystander.conscious' as const },
    { id: 'breathingNormally', key: 'bystander.breathing' as const },
    { id: 'severeBleeding', key: 'bystander.bleeding' as const },
  ];

  return (
    <div>
      <PageHeading eyebrow={t('bystander.limited')} title={t('bystander.title')} description={[resolvedView.patientName, resolvedView.approximateAge].filter(Boolean).join(' · ') || t('common.unknown')} />
      <a className="button button--danger button--full" href={`tel:${number}`}>{t('bystander.call', { number })}</a>
      {language !== 'en' && <p className="permission-note" role="status">{t('action.translationFallback')}</p>}
      <div className="two-column section">
        <section className="card card--raised"><h2>{resolvedView.patientName ?? t('common.unknown')}</h2><dl className="data-list"><div><dt>{t('room.location')}</dt><dd>{resolvedView.location || t('common.unknown')}</dd></div><div><dt>{t('brief.allergies')}</dt><dd>{resolvedView.allergies.join(', ') || t('common.unknown')}</dd></div><div><dt>{t('brief.conditions')}</dt><dd>{resolvedView.conditions.join(', ') || t('common.unknown')}</dd></div><div><dt>{t('brief.medicines')}</dt><dd>{resolvedView.medicines.join(', ') || t('common.unknown')}</dd></div>{resolvedView.emergencyContact && <div><dt>{t('brief.contact')}</dt><dd>{resolvedView.emergencyContact.name} · {resolvedView.emergencyContact.relationship} · <a href={`tel:${resolvedView.emergencyContact.phone.replace(/[^+0-9]/g, '')}`}>{resolvedView.emergencyContact.phone}</a></dd></div>}</dl></section>
        <form className="card stack" onSubmit={(event) => { event.preventDefault(); report.mutate(); }}><h2>{t('bystander.report')}</h2>{fields.map((field) => <fieldset className="fieldset" key={field.id}><legend>{t(field.key)}</legend><div className="answer-grid">{choices.map((choice) => <button key={choice.value} className="answer-button" type="button" aria-pressed={answers[field.id as keyof typeof answers] === choice.value} onClick={() => setAnswers((current) => ({ ...current, [field.id]: choice.value }))}>{choice.label}</button>)}</div></fieldset>)}<button className="button" type="submit" disabled={Object.keys(answers).length === 0 || report.isPending}>{t('common.save')}</button><button className="button button--secondary" type="button" onClick={shareLocation}><MapPin aria-hidden="true" />{t('bystander.location')}</button><p className="field-hint">{t('bystander.consent')}</p></form>
      </div>
      <div className="privacy-lock"><LockKeyhole aria-hidden="true" /><span>{t('bystander.noRecords')}</span></div><p className="disclaimer">{t('common.protocolDisclaimer')}</p><p role="status" aria-live="polite">{notice}</p>
      {protocol && <section className="card section"><h2>{protocol.emergencyCallInstruction}</h2><ol className="plain-list">{protocol.doActions.map((action) => <li key={action.id}>{action.title}</li>)}</ol><h3>{t('action.doNot')}</h3><ul className="plain-list">{protocol.doNotActions.map((action) => <li key={action}>{action}</li>)}</ul><h3>{t('action.escalation')}</h3><p>{protocol.escalationRule}</p><p className="disclaimer">{protocol.disclaimer}</p></section>}
    </div>
  );
}
