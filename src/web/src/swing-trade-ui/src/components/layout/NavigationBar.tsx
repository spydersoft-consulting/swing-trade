import React, { useState } from "react";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faBars, faChartLine, faRightFromBracket, faRightToBracket, faUser } from "@fortawesome/free-solid-svg-icons";
import { NavLink, useNavigate } from "react-router-dom";
import { Sidebar } from "primereact/sidebar";
import { useAuth, usePageTitle } from "../../context";
import { NAV_ITEMS } from "./navItems";

interface NavigationBarProps {
  brand?: string;
}

const navLinkClass = (isActive: boolean) =>
  [
    "flex items-center gap-3 rounded-lg text-sm font-medium transition-colors px-3 min-h-[44px] no-underline",
    isActive ? "bg-brand text-brand-fg" : "text-content-inverse/80 hover:text-content-inverse hover:bg-white/5",
  ].join(" ");

const roleLabel = (roles: readonly string[]) => roles.map((r) => r.replace(/^swingtrade\./, "")).join(", ");

const AccountSection: React.FC<{ dark?: boolean }> = ({ dark }) => {
  const { isAuthenticated, user, roles, login, logout } = useAuth();
  const text = dark ? "text-content-inverse" : "text-content";
  const muted = dark ? "text-content-inverse/60" : "text-content-muted";
  const hover = dark ? "hover:bg-white/5" : "hover:bg-surface-muted";
  const row = `w-full flex items-center gap-3 rounded-lg text-sm font-medium px-3 min-h-[44px] bg-transparent border-0 cursor-pointer ${text} ${hover}`;

  if (!isAuthenticated) {
    return (
      <button type="button" className={row} onClick={login}>
        <FontAwesomeIcon icon={faRightToBracket} aria-hidden="true" />
        <span>Sign in</span>
      </button>
    );
  }
  return (
    <>
      <div className="px-3 py-2" data-testid="account-summary">
        <div className={`flex items-center gap-2 text-sm ${text}`}>
          <FontAwesomeIcon icon={faUser} aria-hidden="true" />
          <span className="truncate">{user?.name || "Account"}</span>
        </div>
        {roles.length > 0 && <p className={`text-meta mt-1 ${muted}`}>Roles: {roleLabel(roles)}</p>}
      </div>
      <button type="button" className={row} onClick={logout}>
        <FontAwesomeIcon icon={faRightFromBracket} aria-hidden="true" />
        <span>Sign out</span>
      </button>
    </>
  );
};

export const NavigationBar: React.FC<NavigationBarProps> = ({ brand = "Swing Trade" }) => {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const navigate = useNavigate();
  const pageTitle = usePageTitle();
  const { access } = useAuth();
  // Without a role there is nothing to navigate to: the app shows only the "Access not granted" page.
  const showNav = access === "granted";

  return (
    <>
      <aside
        className="hidden lg:flex flex-col bg-surface-inverse text-content-inverse sticky top-0 h-screen shrink-0 border-r border-white/5 w-60"
        aria-label="Primary"
      >
        <div className="flex items-center h-16 border-b border-white/5 px-3">
          <button
            type="button"
            onClick={() => navigate("/")}
            className="flex items-center gap-2 bg-transparent text-content-inverse cursor-pointer border-0 p-0"
          >
            <span className="flex h-9 w-9 items-center justify-center rounded-md bg-brand text-brand-fg">
              <FontAwesomeIcon icon={faChartLine} className="text-lg" aria-hidden="true" />
            </span>
            <span className="text-lg font-semibold">{brand}</span>
          </button>
        </div>
        <nav className="flex-1 overflow-y-auto px-2 py-4 space-y-1" aria-label="Screens">
          {showNav &&
            NAV_ITEMS.map((item) => (
              <NavLink key={item.to} to={item.to} className={({ isActive }) => navLinkClass(isActive)}>
                <FontAwesomeIcon icon={item.icon} className="shrink-0" aria-hidden="true" />
                <span>{item.label}</span>
              </NavLink>
            ))}
        </nav>
        <div className="border-t border-white/5 px-2 py-3 space-y-1">
          <AccountSection dark />
        </div>
      </aside>

      <div className="lg:hidden sticky top-0 z-30 bg-surface-inverse text-content-inverse">
        <div className="flex items-center h-14 px-3 gap-2">
          <button
            type="button"
            onClick={() => setDrawerOpen(true)}
            className="h-11 w-11 flex items-center justify-center rounded-md hover:bg-white/10 bg-transparent border-0 cursor-pointer text-content-inverse shrink-0"
            aria-label="Open menu"
          >
            <FontAwesomeIcon icon={faBars} className="text-lg" aria-hidden="true" />
          </button>
          <span className="text-lg font-semibold truncate min-w-0 flex-1">{pageTitle ?? brand}</span>
        </div>
      </div>

      <Sidebar visible={drawerOpen} onHide={() => setDrawerOpen(false)} position="left" className="w-72" showCloseIcon>
        <div className="text-lg font-semibold mb-4">{brand}</div>
        <nav className="space-y-1" aria-label="Screens (menu)">
          {showNav &&
            NAV_ITEMS.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                onClick={() => setDrawerOpen(false)}
                className={({ isActive }) =>
                  [
                    "flex items-center gap-3 px-3 min-h-[48px] rounded-lg text-base font-medium no-underline",
                    isActive ? "bg-brand text-brand-fg" : "text-content hover:bg-surface-muted",
                  ].join(" ")
                }
              >
                <FontAwesomeIcon icon={item.icon} aria-hidden="true" />
                <span>{item.label}</span>
              </NavLink>
            ))}
        </nav>
        <hr className="my-4 border-border" />
        <AccountSection />
      </Sidebar>
    </>
  );
};
