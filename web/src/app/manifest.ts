import type { MetadataRoute } from 'next';

/** Lets people install the web app on a phone or computer (Add to Home Screen / Install app). */
export default function manifest(): MetadataRoute.Manifest {
  return {
    name: 'Avzar',
    short_name: 'Avzar',
    description: 'Run your tailoring business from one place.',
    start_url: '/dashboard',
    scope: '/',
    display: 'standalone',
    background_color: '#FFFFFF',
    theme_color: '#C2410C',
    icons: [
      { src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
      { src: '/icons/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
    ],
  };
}
