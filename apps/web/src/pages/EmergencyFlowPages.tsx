import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
import { AlertTriangle, ArrowLeft, Check, HeartPulse, LocateFixed, Mic, MicOff, Phone, Square } from 'lucide-react';
import { z } from 'zod';
import { api } from '../api';
import { emergencyNumber, PageHeading } from '../components/AppShell';
import { hindiDemoInput } from '../demoData';
import { useAppState } from '../state';
import { incidentCategories, type EmergencyProtocol, type IncidentCategory, type PatientRelationship, type TriState } from '../types';
import { getProtocolTranslation } from '../protocolTranslations';

const categoryTranslationKeys: Record<IncidentCategory, string> = {
  'chest-pain': 'category.chestPain',
  'breathing-difficulty': 'category.breathingDifficulty',
  'fall-injury': 'category.fallInjury',
  unconscious: 'category.unconscious',
  seizure: 'category.seizure',
  'heavy-bleeding': 'category.heavyBleeding',
  'road-accident': 'category.roadAccident',
  'allergic-reaction': 'category.allergicReaction',
  'child-emergency': 'category.childEmergency',
  unknown: 'category.unknown',
};

const relationshipChoices: { value: PatientRelationship; key: string }[] = [
  { value: 'self', key: 'start.self' },
  { value: 'family', key: 'start.family' },
  { value: 'bystander', key: 'start.bystander' },
];

export function StartEmergencyPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { draft, updateDraft, setSession } = useAppState();
  const mutation = useMutation({
    mutationFn: () => api.createSession(draft.category, draft.relationship),
    onSuccess: (session) => {
      setSession(session);
      void navigate('/emergency/capture');
    },
  });

  return (
    <div>
      <PageHeading title={t('start.title')} description={t('start.subtitle')} />
      <form className="stack" onSubmit={(event) => { event.preventDefault(); mutation.mutate(); }}>
        <fieldset className="fieldset">
          <legend className="sr-only">{t('start.title')}</legend>
          <div className="choice-grid">
            {relationshipChoices.map((choice) => <label className="choice-card" key={choice.value}><input type="radio" name="relationship" value={choice.value} checked={draft.relationship === choice.value} onChange={() => updateDraft({ relationship: choice.value })} /><HeartPulse aria-hidden="true" /><span>{t(choice.key)}</span></label>)}
          </div>
        </fieldset>
        <section className="section" aria-labelledby="category-heading">
          <h2 id="category-heading">{t('start.category')}</h2>
          <p className="field-hint">{t('start.categoryHint')}</p>
          <fieldset className="fieldset">
            <legend className="sr-only">{t('start.category')}</legend>
            <div className="category-grid">
              {incidentCategories.map((category) => <label className="choice-card" key={category}><input type="radio" name="category" value={category} checked={draft.category === category} onChange={() => updateDraft({ category })} /><span>{t(categoryTranslationKeys[category])}</span></label>)}
            </div>
          </fieldset>
        </section>
        {mutation.isError && <p className="field-error" role="alert">{t('errors.genericBody')}</p>}
        <div className="button-row"><Link className="button button--ghost" to="/home"><ArrowLeft aria-hidden="true" />{t('common.back')}</Link><button className="button" type="submit" disabled={draft.relationship === 'unknown' || mutation.isPending}>{mutation.isPending ? t('common.loading') : t('start.confirm')}</button></div>
      </form>
    </div>
  );
}

export function IncidentCapturePage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { draft, updateDraft, session, setSession } = useAppState();
  const [recording, setRecording] = useState(false);
  const [recordingSeconds, setRecordingSeconds] = useState(0);
  const [recordedBlob, setRecordedBlob] = useState<Blob | null>(null);
  const [microphoneDenied, setMicrophoneDenied] = useState(false);
  const [locationDenied, setLocationDenied] = useState(false);
  const [error, setError] = useState('');
  const [skipPending, setSkipPending] = useState(false);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const chunksRef = useRef<Blob[]>([]);

  useEffect(() => {
    if (!recording) return;
    const timer = window.setInterval(() => {
      setRecordingSeconds((seconds) => {
        if (seconds >= 29) recorderRef.current?.stop();
        return Math.min(seconds + 1, 30);
      });
    }, 1_000);
    return () => window.clearInterval(timer);
  }, [recording]);

  useEffect(() => () => streamRef.current?.getTracks().forEach((track) => track.stop()), []);

  const startRecording = async () => {
    setError('');
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') {
      setMicrophoneDenied(true);
      return;
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      streamRef.current = stream;
      const recorder = new MediaRecorder(stream, { mimeType: MediaRecorder.isTypeSupported('audio/webm') ? 'audio/webm' : undefined });
      recorderRef.current = recorder;
      chunksRef.current = [];
      recorder.ondataavailable = (event) => { if (event.data.size > 0) chunksRef.current.push(event.data); };
      recorder.onstop = () => {
        const blob = new Blob(chunksRef.current, { type: recorder.mimeType || 'audio/webm' });
        if (blob.size > 0) setRecordedBlob(blob);
        setRecording(false);
        stream.getTracks().forEach((track) => track.stop());
      };
      setRecordingSeconds(0);
      recorder.start(500);
      setRecording(true);
    } catch {
      setMicrophoneDenied(true);
      setRecording(false);
    }
  };

  const stopRecording = () => {
    if (recorderRef.current?.state === 'recording') recorderRef.current.stop();
  };

  const useLocation = () => {
    if (!navigator.geolocation) {
      setLocationDenied(true);
      return;
    }
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => updateDraft({ location: `${coords.latitude.toFixed(5)}, ${coords.longitude.toFixed(5)}` }),
      () => setLocationDenied(true),
      { timeout: 8_000, maximumAge: 60_000, enableHighAccuracy: false },
    );
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError('');
    if (!draft.input.trim() && !recordedBlob) {
      setError(t('auth.invalid'));
      return;
    }
    try {
      let updated = recordedBlob
        ? await api.uploadVoice(session.id, recordedBlob)
        : await api.submitIncident(session.id, draft.input.trim(), draft.location.trim());
      if (recordedBlob && draft.location.trim()) updated = await api.updateLocation(session.id, draft.location.trim());
      setSession(updated);
      void navigate(updated.extraction.criticalMissingQuestions.length > 0 ? '/emergency/questions' : '/emergency/action');
    } catch {
      setError(t('errors.genericBody'));
    }
  };

  const skipAi = async () => {
    if (!draft.input.trim()) { setError(t('auth.invalid')); return; }
    setSkipPending(true); setError('');
    try {
      const updated = await api.submitIncident(session.id, draft.input.trim(), draft.location.trim(), true);
      setSession(updated);
      void navigate('/emergency/action');
    } catch { setError(t('errors.genericBody')); }
    finally { setSkipPending(false); }
  };

  return (
    <div>
      <PageHeading eyebrow={t(categoryTranslationKeys[draft.category])} title={t('capture.title')} description={t('capture.subtitle')} />
      <form className="capture-controls" onSubmit={(event) => void submit(event)}>
        <button className={`record-button ${recording ? 'record-button--active' : ''}`} type="button" onClick={() => recording ? stopRecording() : void startRecording()} aria-pressed={recording}>
          {recording ? <Square aria-hidden="true" /> : <Mic aria-hidden="true" />}{recording ? `${t('capture.stop')} · 0:${String(recordingSeconds).padStart(2, '0')}` : recordedBlob ? `${t('common.done')} · 0:${String(recordingSeconds).padStart(2, '0')}` : t('capture.speak')}
        </button>
        {microphoneDenied && <div className="permission-note" role="status"><MicOff aria-hidden="true" /><span>{t('capture.micDenied')}</span></div>}
        <div className="field"><label htmlFor="incident-input">{t('capture.typed')}</label><textarea id="incident-input" value={draft.input} onChange={(event) => updateDraft({ input: event.target.value })} placeholder={t('capture.placeholder')} maxLength={12_000} /><small>{draft.input.length} / 12,000</small></div>
        <button className="button button--ghost" type="button" onClick={() => updateDraft({ input: hindiDemoInput })}>{t('capture.demo')}</button>
        <div className="card card--muted stack--tight">
          <h2>{t('capture.location')}</h2>
          <button className="button button--secondary" type="button" onClick={useLocation}><LocateFixed aria-hidden="true" />{t('capture.useLocation')}</button>
          <div className="field"><label htmlFor="manual-location">{t('capture.manualLocation')}</label><input id="manual-location" value={draft.location} onChange={(event) => updateDraft({ location: event.target.value })} maxLength={300} autoComplete="street-address" /></div>
          {locationDenied && <p className="permission-note" role="status">{t('capture.locationDenied')}</p>}
        </div>
        {error && <p className="field-error" role="alert">{error}</p>}
        <div className="button-row"><button className="button" type="submit">{t('capture.submit')}</button><button className="button button--secondary" type="button" disabled={skipPending} onClick={() => void skipAi()}>{skipPending ? t('common.loading') : t('capture.skipAi')}</button></div>
      </form>
    </div>
  );
}

export function CriticalQuestionsPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { session, draft, answerQuestion, setSession } = useAppState();
  const questions = session.extraction.criticalMissingQuestions.slice(0, 3);
  const [index, setIndex] = useState(0);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState('');
  const question = questions[index];

  useEffect(() => {
    if (!question) void navigate('/emergency/action', { replace: true });
  }, [navigate, question]);

  if (!question) return null;
  const answer = draft.answers[question.id];

  const next = async () => {
    if (!answer) return;
    if (index < questions.length - 1) {
      setIndex((current) => current + 1);
      return;
    }
    setPending(true);
    setError('');
    try {
      const updated = await api.answerQuestions(session.id, draft.answers);
      setSession({ ...updated, extraction: { ...updated.extraction, isBreathingNormally: draft.answers.breathing ?? updated.extraction.isBreathingNormally, isConscious: draft.answers.conscious ?? updated.extraction.isConscious } });
      void navigate('/emergency/action');
    } catch {
      setError(t('errors.genericBody'));
    } finally {
      setPending(false);
    }
  };

  const answers: { value: TriState; label: string }[] = [{ value: 'yes', label: t('common.yes') }, { value: 'no', label: t('common.no') }, { value: 'unknown', label: t('common.unknown') }];
  return (
    <div>
      <PageHeading title={t('questions.title')} description={t('questions.subtitle')} />
      <div className="progress-steps" aria-hidden="true">{questions.map((item, itemIndex) => <span key={item.id} className={`progress-step ${itemIndex <= index ? 'progress-step--complete' : ''}`} />)}</div>
      <section className="card card--raised question-card" aria-labelledby="question-text">
        <p className="eyebrow">{t('questions.count', { current: index + 1, total: questions.length })}</p>
        <h2 id="question-text" className="question-text">{question.id === 'conscious' ? t('questions.conscious') : question.id === 'breathing' ? t('questions.breathing') : question.id === 'bleeding' ? t('questions.bleeding') : question.question}</h2>
        {!['conscious', 'breathing', 'bleeding'].includes(question.id) && <p className="field-hint">{t('questions.fallback')}</p>}
        <div className="answer-grid">{answers.map((option) => <button key={option.value} className="answer-button" type="button" aria-pressed={answer === option.value} onClick={() => answerQuestion(question.id, option.value)}>{option.label}</button>)}</div>
        {error && <p className="field-error" role="alert">{error}</p>}
        <button className="button" type="button" disabled={!answer || pending} onClick={() => void next()}>{pending ? t('common.loading') : index < questions.length - 1 ? t('questions.next') : t('questions.finish')}</button>
      </section>
    </div>
  );
}

const protocolItemSchema = z.object({
    id: z.string(), version: z.string(), reviewStatus: z.string(), country: z.string(), notice: z.string(), emergencyCallInstruction: z.string(),
    doActions: z.array(z.string()), doNotActions: z.array(z.string()), escalationRule: z.string(), source: z.string(),
  }).passthrough();
const protocolSchema = z.union([z.array(protocolItemSchema), z.object({ protocols: z.array(protocolItemSchema) }).passthrough()]);

function protocolId(category: IncidentCategory, breathing: TriState = 'unknown'): string {
  const ids: Record<IncidentCategory, string> = { 'chest-pain': 'chest-pain', 'breathing-difficulty': 'unknown-emergency', 'fall-injury': 'fall-or-injury', unconscious: breathing === 'no' ? 'unconscious-not-breathing' : 'unconscious-breathing', seizure: 'seizure', 'heavy-bleeding': 'heavy-external-bleeding', 'road-accident': 'unknown-emergency', 'allergic-reaction': 'suspected-allergic-reaction', 'child-emergency': 'unknown-emergency', unknown: 'unknown-emergency' };
  return ids[category];
}

export async function loadCachedProtocol(category: IncidentCategory, language: string, breathing: TriState = 'unknown'): Promise<EmergencyProtocol> {
  const response = await fetch('/api/v1/protocols?country=IN', { credentials: 'omit' });
  if (!response.ok) throw new Error('Protocol catalogue unavailable');
  const parsed = protocolSchema.parse(await response.json());
  const protocols = Array.isArray(parsed) ? parsed : parsed.protocols;
  const raw = protocols.find((protocol) => protocol.id === protocolId(category, breathing)) ?? protocols.find((protocol) => protocol.id === 'unknown-emergency');
  if (!raw) throw new Error('Fallback protocol missing');
  const translation = getProtocolTranslation(language, raw.id);
  const actionText = translation?.actions ?? raw.doActions;
  return { id: raw.id, title: raw.id.replaceAll('-', ' '), version: raw.version, reviewStatus: raw.reviewStatus, country: raw.country, emergencyCallInstruction: translation?.call ?? raw.emergencyCallInstruction, doActions: actionText.map((title, index) => ({ id: `action-${index + 1}`, title, detail: '' })), doNotActions: translation?.doNot ?? raw.doNotActions, escalationRule: translation?.escalation ?? raw.escalationRule, source: raw.source, disclaimer: raw.notice };
}

export function EmergencyActionPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { session, setSession } = useAppState();
  const [step, setStep] = useState(0);
  const [callAttempted, setCallAttempted] = useState(false);
  const [callSaving, setCallSaving] = useState(false);
  const [callError, setCallError] = useState('');
  const number = session.emergencyNumber || emergencyNumber();
  const language = (i18n.resolvedLanguage ?? i18n.language).split('-')[0] ?? 'en';
  const protocolQuery = useQuery({ queryKey: ['protocol', session.category, language, session.extraction.isBreathingNormally], queryFn: () => loadCachedProtocol(session.category, language, session.extraction.isBreathingNormally), staleTime: Infinity, retry: 1 });
  const protocol = protocolQuery.data ?? session.protocol;
  const actions = useMemo(() => protocol ? [{ id: 'emergency-call', title: protocol.emergencyCallInstruction, detail: t('action.noClaim') }, ...protocol.doActions] : [], [protocol, t]);
  const current = actions[step];

  const confirmConnected = async () => {
    setCallSaving(true);
    setCallError('');
    try {
      await api.addTimeline(session.id, 'call-connected', 'Call connection confirmed by the user.');
      const event = { id: crypto.randomUUID(), at: new Date().toISOString(), title: t('action.callConnectedTitle'), detail: t('action.callConnectedDetail'), source: 'confirmed' as const };
      setSession({ ...session, timeline: [...session.timeline, event], updatedAt: event.at });
    } catch {
      setCallError(t('errors.genericBody'));
    } finally {
      setCallSaving(false);
    }
  };

  if (protocolQuery.isPending && !protocol) return <div className="centered-state" role="status"><HeartPulse aria-hidden="true" /><p>{t('common.loading')}</p></div>;
  if (!protocol || !current) return <div className="centered-state"><AlertTriangle aria-hidden="true" /><h1>{t('errors.genericTitle')}</h1><p>{t('common.protocolDisclaimer')}</p><Link className="button" to={`/emergency/${session.id}`}>{t('action.coordinate')}</Link></div>;

  return (
    <div className="emergency-screen">
      <PageHeading eyebrow={`${protocol.title} · ${protocol.version}`} title={t('action.title')} />
      <div className="progress-steps" aria-hidden="true">{actions.map((action, index) => <span key={action.id} className={`progress-step ${index <= step ? 'progress-step--complete' : ''}`} />)}</div>
      <section className="card card--raised action-focus" aria-live="polite">
        <span className="action-focus__number">{step + 1}</span>
        <p className="eyebrow">{t('action.step', { current: step + 1, total: actions.length })}</p>
        <h2>{current.title}</h2>
        {current.detail && <p>{current.detail}</p>}
        {step === 0 ? <><a className="button button--danger" href={`tel:${number}`} onClick={() => { setCallAttempted(true); void api.addTimeline(session.id, 'call-initiated', 'Emergency dial action initiated by the user; connection is not confirmed.').catch(() => undefined); }}><Phone aria-hidden="true" />{t('common.call', { number })}</a>{callAttempted && <button className="button button--secondary" type="button" disabled={callSaving} onClick={() => void confirmConnected()}><Check aria-hidden="true" />{t('action.callConnected')}</button>}{callError && <p className="field-error" role="alert">{callError}</p>}</> : null}
        {step < actions.length - 1 ? <button className="button" type="button" onClick={() => setStep((currentStep) => currentStep + 1)}>{t('action.next')}</button> : <button className="button" type="button" onClick={() => { void navigate(`/emergency/${session.id}`); }}>{t('action.coordinate')}</button>}
      </section>
      <div className="two-column section">
        <aside className="card uncertainty"><h2>{t('action.uncertainty')}</h2><p>{t('action.language', { language: session.extraction.detectedLanguage, confidence: Math.round(session.extraction.languageConfidence * 100) })}</p>{session.extraction.uncertainties.map((uncertainty) => <p key={uncertainty}>{uncertainty}</p>)}</aside>
        <aside className="card"><h2>{t('action.original')}</h2><p className="transcript" lang={/^hi(?:ndi)?$/i.test(session.extraction.detectedLanguage) ? 'hi' : session.extraction.detectedLanguage.toLowerCase()}>{session.originalInput}</p></aside>
      </div>
      <aside className="disclaimer">{protocol.disclaimer}</aside>
      <section className="card section"><h2>{t('action.doNot')}</h2><ul className="plain-list">{protocol.doNotActions.map((action) => <li key={action}>{action}</li>)}</ul><h2>{t('action.escalation')}</h2><p>{protocol.escalationRule}</p></section>
      {!['en', 'hi'].includes(language) && <p className="permission-note" role="status">{t('action.translationFallback')}</p>}
    </div>
  );
}
