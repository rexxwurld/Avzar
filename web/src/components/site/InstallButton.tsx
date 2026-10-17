'use client';

import { useEffect, useState } from 'react';

interface InstallPromptEvent extends Event { prompt: () => Promise<void> }

/** Installs the web app. Uses the browser's install prompt when it offers one; otherwise shows how to do it on this device. */
export function InstallButton() {
  const [prompt, setPrompt] = useState<InstallPromptEvent | null>(null);
  const [note, setNote] = useState('');

  useEffect(() => {
    const onPrompt = (e: Event) => { e.preventDefault(); setPrompt(e as InstallPromptEvent); };
    const onInstalled = () => { setPrompt(null); setNote('Avzar is installed. Open it from your home screen or app list.'); };
    window.addEventListener('beforeinstallprompt', onPrompt);
    window.addEventListener('appinstalled', onInstalled);
    return () => { window.removeEventListener('beforeinstallprompt', onPrompt); window.removeEventListener('appinstalled', onInstalled); };
  }, []);

  async function install() {
    if (prompt) { await prompt.prompt(); setPrompt(null); return; }
    const ua = navigator.userAgent;
    setNote(/iPhone|iPad|iPod/.test(ua)
      ? 'On iPhone or iPad: tap the Share button, then Add to Home Screen.'
      : 'Open your browser menu and choose Install app or Add to Home screen.');
  }

  return (
    <div className="lp-store">
      <button type="button" className="lp-btn lp-btn-alt lp-app-btn" onClick={install}>Install on this device</button>
      <p role="status" className="lp-store-note">{note}</p>
    </div>
  );
}
