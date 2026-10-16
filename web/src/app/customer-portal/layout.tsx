import Link from 'next/link';
import { lpFont } from '../fonts';
import '../app.css';

export default function PortalLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className={`ap ${lpFont.variable}`}>
      <header className="ap-authbar">
        <Link href="/" className="ap-logo">Avzar</Link>
        <span className="ap-foot">Customer portal</span>
      </header>
      {children}
    </div>
  );
}
