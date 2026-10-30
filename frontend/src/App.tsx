import "./App.css";
import { Routes, Route } from "react-router-dom";
import Login from "./pages/Login";
import ProtectedRoute from "./components/ProtectedRoute";
import Admin from "./pages/Admin";
import AdminLayout from "./components/admin/AdminLayout";
import CompanyManagement from "./pages/admin/CompanyManagement";
import CompanyDetails from "./pages/admin/CompanyDetails";
import UserManagement from "./pages/admin/UserManagement";
import BusManagement from "./pages/admin/BusManagement";
import TripManagement from "./pages/admin/TripManagement";
import CompanyLayout from "./components/company/CompanyLayout";
import CompanyOverview from "./pages/company/CompanyOverview";
import CompanyReservations from "./pages/company/CompanyReservations";
import CompanyReports from "./pages/company/CompanyReports";
import CompanyProfile from "./pages/company/CompanyProfile";
import HomePage from "./pages/HomePage";
import SearchResultsPage from "./pages/SearchResultsPage";
import PublicTripDetailsPage from "./pages/PublicTripDetailsPage";
import Register from "./pages/Register";
import FirstLoginPasswordChange from "./pages/FirstLoginPasswordChange";
import Profile from "./pages/Profile";
import "./Site.css";

function App() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route path="/search" element={<SearchResultsPage />} />
      <Route path="/trips/:id" element={<PublicTripDetailsPage />} />
      <Route path="/login" element={<Login />} />
      <Route path="/register" element={<Register />} />
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
        <Route path="buses" element={<BusManagement />} />
        <Route path="trips" element={<TripManagement />} />
        <Route path="reservations" element={<CompanyReservations />} />
        <Route path="reports" element={<CompanyReports />} />
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
        <Route path="buses" element={<BusManagement />} />
        <Route path="trips" element={<TripManagement />} />
      </Route>
    </Routes>
  );
}

export default App;