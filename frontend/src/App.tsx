import "./App.css";
import { lazy, Suspense } from "react";
import { Routes, Route, Navigate } from "react-router-dom";
import Login from "./pages/Login";
import ProtectedRoute from "./components/ProtectedRoute";
import Admin from "./pages/Admin";
import AdminLayout from "./components/admin/AdminLayout";
import CompanyManagement from "./pages/admin/CompanyManagement";
import CompanyDetails from "./pages/admin/CompanyDetails";
import UserManagement from "./pages/admin/UserManagement";
const AdminTickets = lazy(() => import("./pages/admin/AdminTickets"));
const TerminalManagement = lazy(() => import("./pages/admin/TerminalManagement"));
const NotificationManagement = lazy(() => import("./pages/admin/NotificationManagement"));
const BusManagement = lazy(() => import("./pages/admin/BusManagement"));
import TripManagement from "./pages/admin/TripManagement";
const RouteManagement = lazy(() => import("./pages/company/RouteManagement"));
import CompanyLayout from "./components/company/CompanyLayout";
import CompanyOverview from "./pages/company/CompanyOverview";
import CompanyReservations from "./pages/company/CompanyReservations";
import CompanyReports from "./pages/company/CompanyReports";
import CompanyProfile from "./pages/company/CompanyProfile";
import HomePage from "./pages/HomePage";
import SearchResultsPage from "./pages/SearchResultsPage";
import PublicTripDetailsPage from "./pages/PublicTripDetailsPage";
import CheckoutPage from "./pages/CheckoutPage";
const GroupCheckoutPage = lazy(() => import("./pages/GroupCheckoutPage"));
const CampaignManagement = lazy(() => import("./pages/admin/CampaignManagement"));
const CampaignsPage = lazy(() => import("./pages/CampaignsPage"));
import TicketDetailsPage from "./pages/TicketDetailsPage";
import MyReservationsPage from "./pages/MyReservationsPage";
import Register from "./pages/Register";
import FirstLoginPasswordChange from "./pages/FirstLoginPasswordChange";
import Profile from "./pages/Profile";
import { AccountLinkPage, EmailLinkRequestPage } from "./pages/AccountRecovery";
import "./Site.css";

function App() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route path="/search" element={<SearchResultsPage />} />
      <Route path="/campaigns" element={<Suspense fallback={<p role="status">Kampanyalar yükleniyor...</p>}><CampaignsPage /></Suspense>} />
      <Route path="/trips/:id" element={<PublicTripDetailsPage />} />
      <Route path="/tickets" element={<ProtectedRoute allowedRole="Passenger"><MyReservationsPage /></ProtectedRoute>} />
      <Route path="/tickets/:pnr" element={<ProtectedRoute allowedRole="Passenger"><TicketDetailsPage /></ProtectedRoute>} />
      <Route path="/reservations" element={<Navigate to="/tickets" replace />} />
      <Route path="/payment/:id" element={<ProtectedRoute allowedRole="Passenger"><CheckoutPage /></ProtectedRoute>} />
      <Route path="/purchase/:id" element={<ProtectedRoute allowedRole="Passenger"><Suspense fallback={<p role="status">Sipariş yükleniyor...</p>}><GroupCheckoutPage /></Suspense></ProtectedRoute>} />
      <Route path="/test-payment/:id" element={<ProtectedRoute allowedRole="Passenger"><CheckoutPage /></ProtectedRoute>} />
      <Route path="/login" element={<Login />} />
      <Route path="/register" element={<Register />} />
      <Route path="/forgot-password" element={<EmailLinkRequestPage key="forgot" />} />
      <Route path="/resend-activation" element={<EmailLinkRequestPage key="resend" activation />} />
      <Route path="/reset-password" element={<AccountLinkPage />} />
      <Route path="/activate-account" element={<AccountLinkPage activation />} />
      <Route path="/first-login-password-change" element={<FirstLoginPasswordChange />} />

      <Route
        path="/company"
        element={
          <ProtectedRoute allowedRole="Company">
            <CompanyLayout />
          </ProtectedRoute>
        }
      >
        <Route index element={<CompanyOverview />} />
        <Route path="buses" element={<Suspense fallback={<p role="status">Otobüsler yükleniyor...</p>}><BusManagement /></Suspense>} />
        <Route path="trips" element={<TripManagement />} />
        <Route path="routes" element={<Suspense fallback={<p role="status">Güzergâhlar yükleniyor...</p>}><RouteManagement /></Suspense>} />
        <Route path="reservations" element={<CompanyReservations />} />
        <Route path="reports" element={<CompanyReports />} />
        <Route path="campaigns" element={<Suspense fallback={<p role="status">Kampanyalar yükleniyor...</p>}><CampaignManagement /></Suspense>} />
        <Route path="notifications" element={<Suspense fallback={<p role="status">Bildirimler yükleniyor...</p>}><NotificationManagement /></Suspense>} />
        <Route path="profile" element={<CompanyProfile />} />
      </Route>

<Route
  path="/profile"
  element={
    <ProtectedRoute allowedRole="Passenger">
      <Profile />
    </ProtectedRoute>
  }
/>

      <Route
        path="/admin"
        element={
          <ProtectedRoute allowedRole="Admin">
            <AdminLayout />
          </ProtectedRoute>
        }
      >
        <Route index element={<Admin />} />
        <Route path="companies" element={<CompanyManagement />} />
        <Route path="companies/:id" element={<CompanyDetails />} />
        <Route path="users" element={<UserManagement />} />
        <Route path="campaigns" element={<Suspense fallback={<p role="status">Kampanyalar yükleniyor...</p>}><CampaignManagement /></Suspense>} />
        <Route path="tickets" element={<Suspense fallback={<p role="status">Bilet yönetimi yükleniyor...</p>}><AdminTickets /></Suspense>} />
        <Route path="terminals" element={<Suspense fallback={<p role="status">Terminaller yükleniyor...</p>}><TerminalManagement /></Suspense>} />
        <Route path="notifications" element={<Suspense fallback={<p role="status">Bildirimler yükleniyor...</p>}><NotificationManagement /></Suspense>} />
        <Route path="buses" element={<Suspense fallback={<p role="status">Otobüsler yükleniyor...</p>}><BusManagement /></Suspense>} />
        <Route path="trips" element={<TripManagement />} />
        <Route path="routes" element={<Suspense fallback={<p role="status">Güzergâhlar yükleniyor...</p>}><RouteManagement /></Suspense>} />
      </Route>
    </Routes>
  );
}

export default App;
