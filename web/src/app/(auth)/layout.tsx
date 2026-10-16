import Link from 'next/link';
import type { Metadata } from 'next';
import { lpFont } from '../fonts';
import '../app.css';

export const metadata: Metadata = { robots: { index: false, follow: false } };

export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className={`ap ap-auth ${lpFont.variable}`}>
      <header className="ap-authbar">
        <Link href="/" className="ap-logo">Avzar</Link>
        <Link href="/" className="ap-link">Back to home</Link>
      </header>
      <main className="ap-card">{children}</main>
    </div>
  );
}
