import { Outlet } from "react-router-dom";
import { NavigationBar } from "../components/layout/NavigationBar";
import { PageTitleProvider } from "../context";

const MainLayout: React.FC = () => (
  <PageTitleProvider>
    <div className="min-h-screen bg-surface-muted text-content lg:flex">
      <NavigationBar brand="Swing Trade" />
      <div className="flex-1 min-w-0 flex flex-col">
        <main className="flex-1 w-full px-4 lg:px-8 xl:px-12 py-6" id="main">
          <Outlet />
        </main>
      </div>
    </div>
  </PageTitleProvider>
);

export default MainLayout;
