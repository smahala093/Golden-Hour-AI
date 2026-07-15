import { Navigate, Outlet, Route, Routes } from 'react-router-dom';
import { AppShell, PublicShell } from './components/AppShell';
import { ErrorBoundary } from './components/ErrorBoundary';
import { HomePage } from './pages/HomePage';
import { CriticalQuestionsPage, EmergencyActionPage, IncidentCapturePage, StartEmergencyPage } from './pages/EmergencyFlowPages';
import { AnonymousEmergencyPage, AuthPage, LandingPage } from './pages/PublicPages';
import { BystanderPage, CoordinationRoomPage, HospitalHandoverPage, ResponderBriefPage, ShareQrPage, TaskBoardPage } from './pages/SessionPages';
import { AccessibilityPage, ContactsPage, HistoryPage, LanguagePage, OnboardingPage, PrivacyPage, ProfilePage, ReadinessPage, SettingsPage } from './pages/ProfilePages';
import { GenericErrorPage, NotFoundPage, OfflinePage, UnauthorizedPage } from './pages/SystemPages';
import { useAppState } from './state';

function ProtectedLayout() {
  const { authenticated, authChecked } = useAppState();
  if (!authChecked) return <main id="main-content" className="centered-state" role="status">Checking your secure session…</main>;
  if (!authenticated) return <Navigate to="/login" replace />;
  return <AppShell><Outlet /></AppShell>;
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
          <Route path="share/:token" element={<BystanderPage />} />
          <Route path="unauthorized" element={<UnauthorizedPage />} />
        </Route>
        <Route element={<ProtectedLayout />}>
          <Route path="home" element={<HomePage />} />
          <Route path="onboarding" element={<OnboardingPage />} />
          <Route path="profile" element={<ProfilePage />} />
          <Route path="contacts" element={<ContactsPage />} />
          <Route path="privacy" element={<PrivacyPage />} />
          <Route path="emergency/start" element={<StartEmergencyPage />} />
          <Route path="emergency/capture" element={<IncidentCapturePage />} />
          <Route path="emergency/questions" element={<CriticalQuestionsPage />} />
          <Route path="emergency/action" element={<EmergencyActionPage />} />
          <Route path="emergency/:sessionId" element={<CoordinationRoomPage />} />
          <Route path="emergency/:sessionId/tasks" element={<TaskBoardPage />} />
          <Route path="emergency/:sessionId/responder" element={<ResponderBriefPage />} />
          <Route path="emergency/:sessionId/handover" element={<HospitalHandoverPage />} />
          <Route path="emergency/:sessionId/share" element={<ShareQrPage />} />
          <Route path="history" element={<HistoryPage />} />
          <Route path="readiness" element={<ReadinessPage />} />
          <Route path="settings" element={<SettingsPage />} />
          <Route path="settings/language" element={<LanguagePage />} />
          <Route path="settings/accessibility" element={<AccessibilityPage />} />
          <Route path="offline" element={<OfflinePage />} />
          <Route path="error" element={<GenericErrorPage />} />
        </Route>
        <Route path="*" element={<PublicShell><NotFoundPage /></PublicShell>} />
      </Routes>
    </ErrorBoundary>
  );
}
