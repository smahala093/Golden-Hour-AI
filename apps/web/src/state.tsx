import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { applyDocumentLanguage } from './i18n';
import { defaultPreferences, demoExtraction, demoProfile, demoSession, emptyProfile, emptySession } from './demoData';
import { MOCK_MODE, api } from './api';
import { getQueuedUpdates, removeMinimalOfflineCard, saveMinimalOfflineCard } from './offline';
import type { DraftEmergency, EmergencyProfile, EmergencySession, Preferences, TriState } from './types';

const PREFERENCES_KEY = 'gh-preferences-v1';

const initialDraft: DraftEmergency = {
  relationship: 'unknown',
  category: 'unknown',
  input: '',
  location: '',
  answers: {},
};

interface AppStateValue {
  authenticated: boolean;
  authChecked: boolean;
  setAuthenticated(value: boolean): void;
  profile: EmergencyProfile;
  setProfile(profile: EmergencyProfile): void;
  preferences: Preferences;
  updatePreferences(update: Partial<Preferences>): void;
  draft: DraftEmergency;
  updateDraft(update: Partial<DraftEmergency>): void;
  answerQuestion(id: string, answer: TriState): void;
  resetDraft(): void;
  session: EmergencySession;
  setSession(session: EmergencySession): void;
  online: boolean;
  queueCount: number;
  refreshQueueCount(): void;
}

const AppStateContext = createContext<AppStateValue | null>(null);

function loadPreferences(): Preferences {
  if (typeof window === 'undefined') return defaultPreferences;
  try {
    const saved = JSON.parse(window.localStorage.getItem(PREFERENCES_KEY) ?? '{}') as Partial<Preferences>;
    return { ...defaultPreferences, ...saved };
  } catch {
    return defaultPreferences;
  }
}

export function AppStateProvider({ children }: { children: ReactNode }) {
  const { i18n } = useTranslation();
  const [authenticated, setAuthenticated] = useState(MOCK_MODE);
  const [authChecked, setAuthChecked] = useState(MOCK_MODE);
  const [profile, setProfile] = useState(MOCK_MODE ? demoProfile : emptyProfile);
  const [preferences, setPreferences] = useState(loadPreferences);
  const [draft, setDraft] = useState(initialDraft);
  const [session, setSession] = useState(MOCK_MODE ? demoSession : emptySession);
  const [online, setOnline] = useState(() => (typeof navigator === 'undefined' ? true : navigator.onLine));
  const [queueCount, setQueueCount] = useState(() => getQueuedUpdates().length);

  const refreshQueueCount = useCallback(() => setQueueCount(getQueuedUpdates().length), []);

  useEffect(() => {
    if (MOCK_MODE) return;
    void api.getCurrentUser().then(() => setAuthenticated(true)).catch(() => setAuthenticated(false)).finally(() => setAuthChecked(true));
  }, []);

  useEffect(() => {
    if (!authenticated || MOCK_MODE) return;
    void api.getProfile().then(setProfile).catch(() => setProfile(emptyProfile));
  }, [authenticated]);

  useEffect(() => {
    const setOnlineState = () => setOnline(navigator.onLine);
    window.addEventListener('online', setOnlineState);
    window.addEventListener('offline', setOnlineState);
    window.addEventListener('gh-queue-changed', refreshQueueCount);
    return () => {
      window.removeEventListener('online', setOnlineState);
      window.removeEventListener('offline', setOnlineState);
      window.removeEventListener('gh-queue-changed', refreshQueueCount);
    };
  }, [refreshQueueCount]);

  useEffect(() => {
    window.localStorage.setItem(PREFERENCES_KEY, JSON.stringify(preferences));
    window.localStorage.setItem('gh-language', preferences.language);
    void i18n.changeLanguage(preferences.language);
    applyDocumentLanguage(preferences.language);
    document.documentElement.dataset.theme = preferences.theme;
    document.documentElement.classList.toggle('high-contrast', preferences.highContrast);
    document.documentElement.classList.toggle('large-text', preferences.largeText);
    document.documentElement.classList.toggle('reduced-motion', preferences.reducedMotion);
    document.documentElement.classList.toggle('simple-mode', preferences.simpleMode);
    if (preferences.offlineCardEnabled) saveMinimalOfflineCard(profile);
    else removeMinimalOfflineCard();
  }, [i18n, preferences, profile]);

  const updatePreferences = useCallback((update: Partial<Preferences>) => {
    setPreferences((current) => ({ ...current, ...update }));
  }, []);

  const updateDraft = useCallback((update: Partial<DraftEmergency>) => {
    setDraft((current) => ({ ...current, ...update }));
  }, []);

  const answerQuestion = useCallback((id: string, answer: TriState) => {
    setDraft((current) => ({ ...current, answers: { ...current.answers, [id]: answer } }));
    setSession((current) => ({
      ...current,
      extraction: {
        ...current.extraction,
        isBreathingNormally: id === 'breathing' ? answer : current.extraction.isBreathingNormally,
        isConscious: id === 'conscious' ? answer : current.extraction.isConscious,
      },
    }));
  }, []);

  const resetDraft = useCallback(() => setDraft(initialDraft), []);

  const value = useMemo<AppStateValue>(() => ({
    authenticated,
    authChecked,
    setAuthenticated,
    profile,
    setProfile,
    preferences,
    updatePreferences,
    draft,
    updateDraft,
    answerQuestion,
    resetDraft,
    session: session.extraction ? session : { ...(MOCK_MODE ? demoSession : emptySession), extraction: MOCK_MODE ? demoExtraction : emptySession.extraction },
    setSession,
    online,
    queueCount,
    refreshQueueCount,
  }), [answerQuestion, authChecked, authenticated, draft, online, preferences, profile, queueCount, refreshQueueCount, resetDraft, session, updateDraft, updatePreferences]);

  return <AppStateContext.Provider value={value}>{children}</AppStateContext.Provider>;
}

export function useAppState(): AppStateValue {
  const context = useContext(AppStateContext);
  if (!context) throw new Error('useAppState must be used inside AppStateProvider');
  return context;
}
