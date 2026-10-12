import React from "react";
import { useSetPageTitle } from "../../context";

interface PageHeaderProps {
  title: string;
  subtitle?: React.ReactNode;
  actions?: React.ReactNode;
}

export const PageHeader: React.FC<PageHeaderProps> = ({ title, subtitle, actions }) => {
  useSetPageTitle(title);
  // The browser tab shows the page too, so a screen reader and a bookmark both get a meaningful title.
  React.useEffect(() => {
    document.title = `${title} - Swing Trade`;
  }, [title]);

  return (
    <header className="flex flex-row flex-wrap items-center justify-between gap-3 pb-3">
      <div className="min-w-0">
        <h1 className="text-xl lg:text-2xl font-semibold text-content">{title}</h1>
        {subtitle && <p className="text-sm text-content-muted mt-1">{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2 shrink-0 ml-auto">{actions}</div>}
    </header>
  );
};
