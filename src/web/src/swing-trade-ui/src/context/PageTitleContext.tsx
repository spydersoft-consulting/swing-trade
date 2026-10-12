import { createContext, useContext, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";

interface PageTitleContextValue {
  title: string | null;
  setTitle: (title: string | null) => void;
}

const PageTitleContext = createContext<PageTitleContextValue>({
  title: null,
  // eslint-disable-next-line @typescript-eslint/no-empty-function -- default context value is unused; real setter comes from the provider
  setTitle: () => {},
});

export const PageTitleProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const [title, setTitle] = useState<string | null>(null);
  const value = useMemo(() => ({ title, setTitle }), [title]);
  return <PageTitleContext.Provider value={value}>{children}</PageTitleContext.Provider>;
};

export const usePageTitle = (): string | null => useContext(PageTitleContext).title;

export const useSetPageTitle = (title: string | null | undefined): void => {
  const { setTitle } = useContext(PageTitleContext);
  useEffect(() => {
    setTitle(title ?? null);
  }, [title, setTitle]);
};
