import Link from 'next/link';

export default function Home() {
  return (
    <main className="wrap" style={{ padding: '4rem 1rem' }}>
      <h1>Avzar</h1>
      <p>Business tools and websites for tailors and fashion brands. Storefronts are served at your own address.</p>
      <p>
        <Link href="/login">Sign in</Link> · <Link href="/register">Create a business account</Link>
      </p>
    </main>
  );
}
