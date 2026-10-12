import { BrowserRouter as Router, Navigate, Route, Routes } from "react-router-dom";
import { Button } from "primereact/button";
import { ProgressSpinner } from "primereact/progressspinner";
import MainLayout from "../layouts/Main";
import { Watchlist } from "./Watchlist/Watchlist";
import { Trades } from "./Trades/Trades";
import { Sizer } from "./Sizer/Sizer";
import { Dashboard } from "./Dashboard/Dashboard";
import { Journal } from "./Journal/Journal";
import { Settings } from "./Settings/Settings";
import { AccessNotGranted } from "./AccessNotGranted/AccessNotGranted";
import { Landing } from "./Landing/Landing";
import { useAuth } from "../context";

const Spinner = () => (
  <div className="flex justify-center py-16" role="status" aria-label="Loading">
    <ProgressSpinner />
  </div>
);

export const AppRouter: React.FC = () => {
  const { isAuthenticated, isLoading, access, refreshAuth } = useAuth();

  if (isLoading) return <Spinner />;
  if (!isAuthenticated) return <Landing />;
  if (access === "unknown") return <Spinner />;

  return (
    <Router basename="/">
      <Routes>
        <Route element={<MainLayout />}>
          {access === "denied" && <Route path="*" element={<AccessNotGranted />} />}
          {access === "error" && (
            <Route
              path="*"
              element={
                <div className="max-w-xl mx-auto py-10 space-y-3" role="alert">
                  <h1 className="text-xl font-semibold">Could not check your access</h1>
                  <p className="text-content-muted">The server did not answer the role check. Try again in a moment.</p>
                  <Button label="Try again" icon="pi pi-refresh" onClick={() => void refreshAuth()} />
                </div>
              }
            />
          )}
          {access === "granted" && (
            <>
              <Route path="/" element={<Navigate to="/watchlist" replace />} />
              <Route path="/watchlist" element={<Watchlist />} />
              <Route path="/trades" element={<Trades />} />
              <Route path="/sizer" element={<Sizer />} />
              <Route path="/dashboard" element={<Dashboard />} />
              <Route path="/journal" element={<Journal />} />
              <Route path="/settings" element={<Settings />} />
              <Route path="*" element={<Navigate to="/watchlist" replace />} />
            </>
          )}
        </Route>
      </Routes>
    </Router>
  );
};
