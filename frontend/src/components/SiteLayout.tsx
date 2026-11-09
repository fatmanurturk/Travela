import { Outlet } from "react-router-dom";
import SiteFooter from "./SiteFooter";
import "./SiteLayout.css";

function SiteLayout() {
  return (
    <div className="site-layout">
      <div className="site-layout-content">
        <Outlet />
      </div>
      <SiteFooter />
    </div>
  );
}

export default SiteLayout;
