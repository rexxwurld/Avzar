import Link from 'next/link';
import type { Metadata } from 'next';
import '../app.css';

export const metadata: Metadata = { robots: { index: false, follow: false } };

export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="ap ap-auth">
      <main className="ap-card">
        <Link href="/" className="ap-brand">Avzar</Link>
        {children}
      </main>
    </div>
  );
}
