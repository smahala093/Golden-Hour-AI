import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { applyDocumentLanguage } from './i18n';
import { defaultPreferences, demoExtraction, demoProfile, demoSession, emptyProfile, emptySession } from './demoData';
import { ApiError, MOCK_MODE, api } from './api';
import { clearUserScopedOfflineData, getQueuedUpdates, removeMinimalOfflineCard, saveMinimalOfflineCard } from './offline';
import type { DraftEmergency, EmergencyProfile, EmergencySession, Preferences, TriState } from './types';

const PREFERENCES_KEY = 'gh-preferences-v1';

const initialDraft: DraftEmergency = {
  relationship: 'unknown',
  category: 'unknown',
  useOwnerProfileForPatient: false,
  input: '',
  location: '',
  answers: {},
};

interface AppStateValue {
  authenticated: boolean;
  authChecked: boolean;
  authUnavailable: boolean;
  profileBootstrapComplete: boolean;
  setAuthenticated: (value: boolean) => void;
  clearUserState: () => Promise<void>;
  profile: EmergencyProfile;
  setProfile: (profile: EmergencyProfile) => void;
  preferences: Preferences;
  updatePreferences: (update: Partial<Preferences>) => void;
  draft: DraftEmergency;
  updateDraft: (update: Partial<DraftEmergency>) => void;
  answerQuestion: (id: string, answer: string) => void;
  resetDraft: () => void;
  session: EmergencySession;
  setSession: (session: EmergencySession) => void;
  online: boolean;
  queueCount: number;
  refreshQueueCount: () => void;
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
  const [authenticated, setAuthenticatedState] = useState(MOCK_MODE);
  const [authChecked, setAuthChecked] = useState(MOCK_MODE);
  const [authUnavailable, setAuthUnavailable] = useState(false);
  const [profile, setProfileState] = useState(MOCK_MODE ? demoProfile : emptyProfile);
  const [profileHydrated, setProfileHydrated] = useState(MOCK_MODE);
  const [profileBootstrapComplete, setProfileBootstrapComplete] = useState(MOCK_MODE);
  const [preferences, setPreferences] = useState(loadPreferences);
  const [draft, setDraft] = useState(initialDraft);
  const [session, setSession] = useState(MOCK_MODE ? demoSession : emptySession);
  const [online, setOnline] = useState(() => (typeof navigator === 'undefined' ? true : navigator.onLine));
  const [queueCount, setQueueCount] = useState(() => getQueuedUpdates().length);

  const refreshQueueCount = useCallback(() => setQueueCount(getQueuedUpdates().length), []);

  const clearInMemoryUserState = useCallback(() => {
    setProfileState(emptyProfile);
    setProfileHydrated(false);
    setProfileBootstrapComplete(false);
    setDraft(initialDraft);
    setSession(emptySession);
  }, []);

  const clearUserState = useCallback(() => {
    clearInMemoryUserState();
    return clearUserScopedOfflineData();
  }, [clearInMemoryUserState]);

  const setAuthenticated = useCallback((value: boolean) => {
    if (!value) {
      void clearUserState();
      setAuthenticatedState(false);
      return;
    }
    if (MOCK_MODE) {
      setProfileState(demoProfile);
      setProfileHydrated(true);
      setProfileBootstrapComplete(true);
      setSession(demoSession);
      setDraft(initialDraft);
    } else {
      clearInMemoryUserState();
    }
    setAuthUnavailable(false);
    setAuthenticatedState(true);
  }, [clearInMemoryUserState, clearUserState]);

  useEffect(() => {
    if (MOCK_MODE) return;
    if (!online) {
      clearInMemoryUserState();
      setAuthenticatedState(false);
      setAuthUnavailable(true);
      setAuthChecked(true);
      return;
    }
    setAuthChecked(false);
    void api.getCurrentUser()
      .then(() => {
        clearInMemoryUserState();
        setAuthenticatedState(true);
        setAuthUnavailable(false);
      })
      .catch((error: unknown) => {
        if (error instanceof ApiError && error.status === 401) void clearUserState();
        else clearInMemoryUserState();
        setAuthenticatedState(false);
        setAuthUnavailable(!(error instanceof ApiError && error.status === 401));
      })
      .finally(() => setAuthChecked(true));
  }, [clearInMemoryUserState, clearUserState, online]);

  useEffect(() => {
    if (!online) return;
    void fetch('/api/v1/protocols?country=IN', { credentials: 'omit' }).catch(() => undefined);
  }, [online]);

  useEffect(() => {
    if (!authenticated || MOCK_MODE) return;
    let active = true;
    setProfileBootstrapComplete(false);
    void api.getProfile()
      .then((loadedProfile) => {
        if (!active) return;
        setProfileState(loadedProfile);
        setProfileHydrated(true);
        setProfileBootstrapComplete(true);
      })
      .catch(() => {
        if (!active) return;
        setProfileState(emptyProfile);
        setProfileHydrated(false);
        setProfileBootstrapComplete(true);
      });
    return () => { active = false; };
  }, [authenticated]);

  useEffect(() => {
    const handleAuthExpired = () => {
      void clearUserState();
      setAuthenticatedState(false);
      setAuthUnavailable(false);
      setAuthChecked(true);
    };
    window.addEventListener('gh-auth-expired', handleAuthExpired);
    return () => window.removeEventListener('gh-auth-expired', handleAuthExpired);
  }, [clearUserState]);

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
    if (!preferences.offlineCardEnabled) void removeMinimalOfflineCard();
    else if (profileHydrated) void saveMinimalOfflineCard(profile);
  }, [i18n, preferences, profile, profileHydrated]);

  const setProfile = useCallback((nextProfile: EmergencyProfile) => {
    setProfileState(nextProfile);
    setProfileHydrated(true);
    setProfileBootstrapComplete(true);
  }, []);

  const updatePreferences = useCallback((update: Partial<Preferences>) => {
    setPreferences((current) => ({ ...current, ...update }));
  }, []);

  const updateDraft = useCallback((update: Partial<DraftEmergency>) => {
    setDraft((current) => ({ ...current, ...update }));
  }, []);

  const answerQuestion = useCallback((id: string, answer: string) => {
    setDraft((current) => ({ ...current, answers: { ...current.answers, [id]: answer } }));
    const triState: TriState | undefined = answer === 'yes' || answer === 'no' || answer === 'unknown' ? answer : undefined;
    setSession((current) => ({
      ...current,
      extraction: {
        ...current.extraction,
        isBreathingNormally: id === 'breathing' && triState ? triState : current.extraction.isBreathingNormally,
        isConscious: id === 'conscious' && triState ? triState : current.extraction.isConscious,
      },
    }));
  }, []);

  const resetDraft = useCallback(() => setDraft(initialDraft), []);

  const value = useMemo<AppStateValue>(() => ({
    authenticated,
    authChecked,
    authUnavailable,
    profileBootstrapComplete,
    setAuthenticated,
    clearUserState,
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
  }), [answerQuestion, authChecked, authUnavailable, authenticated, clearUserState, draft, online, preferences, profile, profileBootstrapComplete, queueCount, refreshQueueCount, resetDraft, session, setAuthenticated, setProfile, updateDraft, updatePreferences]);

  return <AppStateContext.Provider value={value}>{children}</AppStateContext.Provider>;
}

export function useAppState(): AppStateValue {
  const context = useContext(AppStateContext);
  if (!context) throw new Error('useAppState must be used inside AppStateProvider');
  return context;
}
