import React from "react";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faChartLine, faRightToBracket } from "@fortawesome/free-solid-svg-icons";
import { Button } from "../ui/Button";
import { useAuth } from "../../context";

export const Landing: React.FC = () => {
  const { login } = useAuth();

  return (
    <div className="min-h-screen bg-surface-muted text-content flex flex-col">
      <header className="bg-surface-inverse text-content-inverse">
        <div className="max-w-5xl mx-auto px-4 lg:px-8 h-16 flex items-center gap-2">
          <span className="flex h-9 w-9 items-center justify-center rounded-md bg-brand text-brand-fg">
            <FontAwesomeIcon icon={faChartLine} className="text-lg" aria-hidden="true" />
          </span>
          <span className="text-xl font-semibold">Swing Trade</span>
        </div>
      </header>
      <main className="flex-1 flex items-center">
        <div className="w-full max-w-5xl mx-auto px-4 lg:px-8 py-12">
          <h1 className="text-3xl sm:text-4xl font-semibold mb-3">A weekly swing-trading routine, in one hour.</h1>
          <p className="text-lg text-content-muted mb-8 max-w-xl">
            Watchlist signals from fixed rules, a position sizer, your trades and a pass log. Every number comes from
            the server.
          </p>
          <Button label="Sign in" icon={<FontAwesomeIcon icon={faRightToBracket} />} size="lg" onClick={login} />
        </div>
      </main>
    </div>
  );
};
