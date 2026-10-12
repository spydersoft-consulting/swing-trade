import React from "react";
import { PrimeReactProvider as PrimeProvider } from "primereact/api";

interface PrimeReactProviderProps {
  children: React.ReactNode;
}

export const PrimeReactProvider: React.FC<PrimeReactProviderProps> = ({ children }) => {
  const value = {
    ripple: true,
    inputStyle: "outlined" as const,
    locale: "en",
    cssTransition: true,
    autoZIndex: true,
    zIndex: {
      modal: 1100,
      overlay: 1000,
      menu: 1000,
      tooltip: 1100,
      toast: 1200,
    },
  };

  return <PrimeProvider value={value}>{children}</PrimeProvider>;
};
