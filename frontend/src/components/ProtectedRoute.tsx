import { Navigate, useLocation } from "react-router-dom";
import type { ReactNode } from "react";

type ProtectedRouteProps = {
  children: ReactNode;
  allowedRole: string;
};

function ProtectedRoute({
  children,
  allowedRole,
}: ProtectedRouteProps) {
  const location = useLocation();
  const loginPath = `/login?returnTo=${encodeURIComponent(location.pathname)}`;
  const token = sessionStorage.getItem("travela_token");
  const storedUser = sessionStorage.getItem("travela_user");

  if (!token || !storedUser) {
    return <Navigate to={loginPath} replace />;
  }

  let user;

  try {
    user = JSON.parse(storedUser);
  } catch {
    return <Navigate to={loginPath} replace />;
  }

  if (!user || user.role !== allowedRole) {
    return <Navigate to="/" replace />;
  }

  if (user.mustChangePassword) {
    return <Navigate to="/first-login-password-change" replace />;
  }

  return <>{children}</>;
}

export default ProtectedRoute;
