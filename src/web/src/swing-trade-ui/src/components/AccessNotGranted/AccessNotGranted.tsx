import React from "react";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faLock, faRightFromBracket } from "@fortawesome/free-solid-svg-icons";
import { Button } from "../ui/Button";
import { useAuth } from "../../context";
import { useSetPageTitle } from "../../context/PageTitleContext";
import { ROLE_DESCRIPTIONS, ROLE_NAMES } from "../../utils/roles";

/** Shown to a signed-in user the API answers 403 for: no swingtrade.* role. Names what to ask for. */
export const AccessNotGranted: React.FC = () => {
  const { user, logout } = useAuth();
  useSetPageTitle("Access not granted");

  return (
    <div className="max-w-2xl mx-auto py-8" data-testid="access-not-granted">
      <div className="rounded-xl bg-surface border border-border p-6 space-y-4">
        <div className="flex items-center gap-3">
          <FontAwesomeIcon icon={faLock} className="text-2xl text-bad" aria-hidden="true" />
          <h1 className="text-xl font-semibold m-0">Access not granted</h1>
        </div>
        <p className="text-content-muted m-0">
          {user?.name ? `You are signed in as ${user.name}, but ` : "You are signed in, but "}
          your account has none of the Swing Trade roles, so there is nothing here to show. Ask the owner to grant one
          of these roles in the identity server, then sign in again:
        </p>
        <ul className="list-none p-0 m-0 space-y-2">
          {(Object.keys(ROLE_NAMES) as (keyof typeof ROLE_NAMES)[]).map((key) => (
            <li key={key} className="rounded-lg border border-border p-3">
              <code className="font-numeric font-semibold">{ROLE_NAMES[key]}</code>
              <p className="text-sm text-content-muted m-0 mt-1">{ROLE_DESCRIPTIONS[key]}</p>
            </li>
          ))}
        </ul>
        <p className="text-sm text-content-muted m-0">
          Most people need swingtrade.reader to look, or swingtrade.trader to log trades.
        </p>
        <Button
          type="button"
          label="Sign out"
          icon={<FontAwesomeIcon icon={faRightFromBracket} />}
          variant="outlined"
          onClick={logout}
        />
      </div>
    </div>
  );
};
