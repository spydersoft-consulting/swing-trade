import {
  faBook,
  faBriefcase,
  faCalculator,
  faChartLine,
  faCog,
  faList,
  type IconDefinition,
} from "@fortawesome/free-solid-svg-icons";

export interface NavItem {
  label: string;
  icon: IconDefinition;
  to: string;
}

export const NAV_ITEMS: NavItem[] = [
  { label: "Watchlist", icon: faList, to: "/watchlist" },
  { label: "Trades", icon: faBriefcase, to: "/trades" },
  { label: "Sizer", icon: faCalculator, to: "/sizer" },
  { label: "Dashboard", icon: faChartLine, to: "/dashboard" },
  { label: "Journal", icon: faBook, to: "/journal" },
  { label: "Settings", icon: faCog, to: "/settings" },
];
