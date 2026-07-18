import { useEffect, useLayoutEffect, useState } from 'react';
import { Navigate, Outlet, Route, Routes, useLocation, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from './api';
import { AppShell, emergencyNumber, PublicShell } from './components/AppShell';
import { ErrorBoundary } from './components/ErrorBoundary';
import { HomePage } from './pages/HomePage';
import { CriticalQuestionsPage, EmergencyActionPage, IncidentCapturePage, StartEmergencyPage } from './pages/EmergencyFlowPages';
import { AnonymousEmergencyPage, AuthPage, LandingPage } from './pages/PublicPages';
import { BystanderPage, CoordinationRoomPage, FamilySummaryPage, HospitalHandoverPage, JoinParticipantPage, ResponderBriefPage, ShareQrPage, TaskBoardPage } from './pages/SessionPages';
import { AccessibilityPage, ContactsPage, HistoryPage, LanguagePage, OnboardingPage, PrivacyPage, ProfilePage, ReadinessPage, SettingsPage } from './pages/ProfilePages';
import { GenericErrorPage, NotFoundPage, OfflinePage, UnauthorizedPage } from './pages/SystemPages';
import { captureAndScrubParticipantInvite, participantInviteSessionId } from './participantInvite';
import { useAppState } from './state';

function ProtectedLayout() {
  const { t } = useTranslation();
  const { authenticated, authChecked, authUnavailable, online, profileBootstrapComplete } = useAppState();
  const location = useLocation();
  const inviteSessionId = participantInviteSessionId(location.pathname);
  useLayoutEffect(() => {
    if (inviteSessionId && location.hash) captureAndScrubParticipantInvite(inviteSessionId, location.hash);
  }, [inviteSessionId, location.hash]);
  if (!authChecked) return <main id="main-content" className="centered-state" role="status">{t('auth.checking')}</main>;
  if (!authenticated && (authUnavailable || !online)) return <Navigate to="/offline" replace />;
  if (!authenticated) return <Navigate to="/login" state={{ from: inviteSessionId ? location.pathname : `${location.pathname}${location.search}${location.hash}` }} replace />;
  if (!profileBootstrapComplete) return <main id="main-content" className="centered-state" role="status">{t('auth.checking')}</main>;
  return <AppShell><Outlet /></AppShell>;
}

function SessionFlowLoader() {
  const { t } = useTranslation();
  const { sessionId } = useParams();
  const { session, setSession, online } = useAppState();
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (!sessionId || session.id === sessionId || !online) return;
    setFailed(false);
    void api.getSession(sessionId).then(setSession).catch(() => setFailed(true));
  }, [online, session.id, sessionId, setSession]);

  if (!sessionId) return <Navigate to="/history" replace />;
  if (session.id === sessionId) return <Outlet />;
  if (failed || !online) return <section className="centered-state" role="alert"><h1>{t('errors.genericTitle')}</h1><p>{online ? t('errors.unauthorizedBody') : t('offline.body', { number: emergencyNumber() })}</p><a className="button button--danger" href={`tel:${emergencyNumber()}`}>{t('common.call', { number: emergencyNumber() })}</a></section>;
  return <section className="centered-state" role="status"><p>{t('common.loading')}</p></section>;
}

function PublicLayout() {
  return <PublicShell><Outlet /></PublicShell>;
}

export function App() {
  return (
    <ErrorBoundary>
      <Routes>
        <Route element={<PublicLayout />}>
          <Route index element={<LandingPage />} />
          <Route path="login" element={<AuthPage mode="login" />} />
          <Route path="register" element={<AuthPage mode="register" />} />
          <Route path="help-nearby" element={<AnonymousEmergencyPage />} />
          <Route path="share" element={<BystanderPage />} />
          <Route path="unauthorized" element={<UnauthorizedPage />} />
          <Route path="offline" element={<OfflinePage />} />
        </Route>
        <Route element={<ProtectedLayout />}>
          <Route path="home" element={<HomePage />} />
          <Route path="onboarding" element={<OnboardingPage />} />
          <Route path="profile" element={<ProfilePage />} />
          <Route path="contacts" element={<ContactsPage />} />
          <Route path="privacy" element={<PrivacyPage />} />
          <Route path="emergency/start" element={<StartEmergencyPage />} />
          <Route path="emergency/:sessionId/join" element={<JoinParticipantPage />} />
          <Route path="emergency/:sessionId" element={<SessionFlowLoader />}>
            <Route index element={<CoordinationRoomPage />} />
            <Route path="capture" element={<IncidentCapturePage />} />
            <Route path="questions" element={<CriticalQuestionsPage />} />
            <Route path="action" element={<EmergencyActionPage />} />
            <Route path="tasks" element={<TaskBoardPage />} />
            <Route path="family-summary" element={<FamilySummaryPage />} />
            <Route path="responder" element={<ResponderBriefPage />} />
            <Route path="handover" element={<HospitalHandoverPage />} />
            <Route path="share" element={<ShareQrPage />} />
          </Route>
          <Route path="history" element={<HistoryPage />} />
          <Route path="readiness" element={<ReadinessPage />} />
          <Route path="settings" element={<SettingsPage />} />
          <Route path="settings/language" element={<LanguagePage />} />
          <Route path="settings/accessibility" element={<AccessibilityPage />} />
          <Route path="error" element={<GenericErrorPage />} />
        </Route>
        <Route path="*" element={<PublicShell><NotFoundPage /></PublicShell>} />
      </Routes>
    </ErrorBoundary>
  );
}
