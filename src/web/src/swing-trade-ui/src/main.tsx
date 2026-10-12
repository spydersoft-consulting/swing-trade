import "./styles.css";
import "primereact/resources/themes/lara-light-blue/theme.css";
import "primereact/resources/primereact.min.css";
import "primeicons/primeicons.css";
import "./api/client";

import React from "react";
import ReactDOM from "react-dom/client";
import { Provider } from "react-redux";
import { AppRouter } from "./components/AppRouter";
import { AuthProvider, PrimeReactProvider, ToastProvider } from "./context";
import { store } from "./store/store";

const root = ReactDOM.createRoot(document.getElementById("root") as HTMLElement);

root.render(
  <React.StrictMode>
    <PrimeReactProvider>
      <ToastProvider>
        <AuthProvider>
          <Provider store={store}>
            <AppRouter />
          </Provider>
        </AuthProvider>
      </ToastProvider>
    </PrimeReactProvider>
  </React.StrictMode>,
);
