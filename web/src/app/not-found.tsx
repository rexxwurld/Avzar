import Link from 'next/link';

export default function NotFound() {
  return (
    <main className="wrap" style={{ padding: '4rem 1rem' }}>
      <h1>Page not found</h1>
      <p>This page does not exist or is not published.</p>
      <Link href="/">Back to the start</Link>
    </main>
  );
}
