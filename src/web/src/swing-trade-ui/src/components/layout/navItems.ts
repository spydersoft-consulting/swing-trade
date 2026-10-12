export interface NavItem {
  label: string;
  icon: string;
  to: string;
}

export const NAV_ITEMS: NavItem[] = [
  { label: "Watchlist", icon: "pi-list", to: "/watchlist" },
  { label: "Trades", icon: "pi-briefcase", to: "/trades" },
  { label: "Sizer", icon: "pi-calculator", to: "/sizer" },
  { label: "Dashboard", icon: "pi-chart-line", to: "/dashboard" },
  { label: "Journal", icon: "pi-book", to: "/journal" },
  { label: "Settings", icon: "pi-cog", to: "/settings" },
];
