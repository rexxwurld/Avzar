'use client';

import { useCallback, useEffect, useMemo, useState, type ChangeEvent, type FormEvent } from 'react';
import { api, apiUpload } from '@/lib/client';
import { messageFor } from '@/lib/problem';
import { reorderItems } from '@/lib/storefront-editor';

type SiteSummary = { id: string; name: string; slug: string; status: string; templateKey: string; publishedVersion: number | null; draftRevision: number; hasUnpublishedChanges: boolean; updatedAtUtc: string };
type Catalog = { templates: { key: string; name: string; description: string; tier: string; allowed: boolean; supported: Record<string, string[]> }[]; sectionTypes: { type: string; label: string; minLevel: string; variants: string[]; allowed: boolean; props: { name: string; kind: string; max: number; min: number; required: boolean; multiline: boolean; itemMax: number; fields: unknown[] | null }[] }[]; themes: { key: string; name: string; colors: Record<string, string>; headingFont: string; bodyFont: string }[]; fonts: string[]; presets: { key: string; name: string; sectionType: string; variant: string; props: unknown; style: unknown; isBuiltIn: boolean }[]; capabilities: { builder: boolean; themeLevel: string; brandingLevel: string; sectionLevel: string; templateTier: string; customPages: boolean; customDomain: boolean; maxPages: number | null; maxStorefronts: number | null } };
type Draft = { storefrontId: string; revision: number; document: unknown; theme: unknown; contrastWarnings: { foreground: string; background: string; ratio: number; required: number }[]; planIssues: { code: string; message: string }[] };
type SiteDetails = { id: string; name: string; slug: string; status: string; templateKey: string; publishedVersion: number | null; draftRevision: number; hasUnpublishedChanges: boolean; contactEmail?: string | null; contactPhone?: string | null; address?: string | null; openingHours?: string | null; workflowInquiry: boolean; workflowBooking: boolean; workflowCustomOrder: boolean; workflowContact: boolean; allowSearchIndexing: boolean; version: number; publicUrlPath: string };
type Version = { versionNumber: number; templateKey: string; note?: string | null; publishedAtUtc: string; isLive: boolean; rolledBackFromVersion?: number | null };
type SwitchPlan = { fromTemplate: string; toTemplate: string; needsConfirmation: boolean; kept: { sectionId: string; type: string; fromVariant?: string | null; toVariant?: string | null; page: string }[]; converted: { sectionId: string; type: string; fromVariant?: string | null; toVariant?: string | null; page: string }[]; archived: { sectionId: string; type: string; fromVariant?: string | null; toVariant?: string | null; page: string }[]; restored: { sectionId: string; type: string; fromVariant?: string | null; toVariant?: string | null; page: string }[]; token: string };
type CustomDomain = { id: string; hostname: string; status: string; verificationRecordName: string; verificationRecordValue: string; verifiedAtUtc?: string | null; lastCheckedAtUtc?: string | null; lastError?: string | null };
type MediaAsset = { id: string; purpose: string; fileName: string; width: number; height: number; sizeBytes: number; createdAtUtc: string; inUse: boolean };
type Paged<T> = { items: T[]; page: number; pageSize: number; totalCount: number };
type PageDoc = { id: string; kind: string; slug: string; title: string; visible: boolean; seo?: { title?: string | null; description?: string | null; shareImageMediaId?: string | null }; sections: SectionDoc[] };
type SectionDoc = { id: string; type: string; variant: string; visible: boolean; props: Record<string, unknown>; style?: Record<string, unknown> };
type DocumentDoc = { schemaVersion: number; templateKey: string; theme: unknown; branding: Record<string, unknown>; navigation: { label: string; pageId: string; visible: boolean }[]; pages: PageDoc[] };

export function StorefrontManager({ initialSites, catalog, canEdit, canPublish, canViewMedia, canManageMedia }: { initialSites: SiteSummary[]; catalog: Catalog; canEdit: boolean; canPublish: boolean; canViewMedia: boolean; canManageMedia: boolean }) {
  const [sites, setSites] = useState(initialSites);
  const [selectedId, setSelectedId] = useState(initialSites[0]?.id ?? '');
  const [details, setDetails] = useState<SiteDetails | null>(null);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [versions, setVersions] = useState<Version[]>([]);
  const [domains, setDomains] = useState<CustomDomain[]>([]);
  const [mediaAssets, setMediaAssets] = useState<MediaAsset[]>([]);
  const [selectedMediaId, setSelectedMediaId] = useState('');
  const [draggedSectionIndex, setDraggedSectionIndex] = useState<number | null>(null);
  const [switchPlan, setSwitchPlan] = useState<SwitchPlan | null>(null);
  const [targetTemplate, setTargetTemplate] = useState('');
  const [newDomain, setNewDomain] = useState('');
  const [pageIndex, setPageIndex] = useState(0);
  const [sectionIndex, setSectionIndex] = useState(0);
  const [newName, setNewName] = useState('');
  const [newTemplate, setNewTemplate] = useState(catalog.templates.find((t) => t.allowed)?.key ?? 'standard');
  const [propsText, setPropsText] = useState('');
  const [styleText, setStyleText] = useState('');
  const [presetKey, setPresetKey] = useState('');
  const [themeText, setThemeText] = useState('');
  const [settingsName, setSettingsName] = useState('');
  const [contactEmail, setContactEmail] = useState('');
  const [contactPhone, setContactPhone] = useState('');
  const [address, setAddress] = useState('');
  const [openingHours, setOpeningHours] = useState('');
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');

  const refresh = useCallback(async (id: string) => {
    if (!id) { setDetails(null); setDraft(null); setVersions([]); setDomains([]); setSwitchPlan(null); return; }
    setBusy(true); setError('');
    try {
      const [d, dr, vs, ds] = await Promise.all([
        api<SiteDetails>(`/api/storefronts/${id}`),
        api<Draft>(`/api/storefronts/${id}/draft`),
        api<Version[]>(`/api/storefronts/${id}/versions`),
        api<CustomDomain[]>(`/api/storefronts/${id}/domains`),
      ]);
      setDetails(d); setDraft(dr); setVersions(vs); setDomains(ds); setTargetTemplate(d.templateKey); setSwitchPlan(null); setSettingsName(d.name);
      setContactEmail(d.contactEmail ?? ''); setContactPhone(d.contactPhone ?? ''); setAddress(d.address ?? ''); setOpeningHours(d.openingHours ?? '');
      setPageIndex(0); setSectionIndex(0); setThemeText(JSON.stringify((dr.document as DocumentDoc).theme ?? {}, null, 2));
      const doc = dr.document as DocumentDoc;
      setPropsText(JSON.stringify(doc.pages?.[0]?.sections?.[0]?.props ?? {}, null, 2));
      setStyleText(JSON.stringify(doc.pages?.[0]?.sections?.[0]?.style ?? {}, null, 2));
    } catch (e) { setError(messageFor(e)); }
    finally { setBusy(false); }
  }, []);

  useEffect(() => { void refresh(selectedId); }, [selectedId, refresh]);
  useEffect(() => {
    if (!canViewMedia) { setMediaAssets([]); return; }
    void api<Paged<MediaAsset>>('/api/media?page=1&pageSize=100').then((result) => setMediaAssets(result.items)).catch(() => setMediaAssets([]));
  }, [canViewMedia]);

  const document = draft?.document as DocumentDoc | undefined;
  const page = document?.pages?.[pageIndex];
  const section = page?.sections?.[sectionIndex];
  const currentTemplate = useMemo(() => catalog.templates.find((t) => t.key === details?.templateKey), [catalog.templates, details?.templateKey]);
  const selectedPreset = catalog.presets.find((p) => p.key === presetKey) ?? catalog.presets[0];
  const sectionSupportsMedia = !!section && (section.type === 'gallery' || !!catalog.sectionTypes.find((x) => x.type === section.type)?.props.some((prop) => prop.name === 'imageMediaId'));

  function updateDocument(mutator: (doc: DocumentDoc) => DocumentDoc) {
    if (!draft) return;
    setDraft({ ...draft, document: mutator(structuredClone(draft.document as DocumentDoc)) });
  }
  function updatePage(patch: Partial<PageDoc>) {
    updateDocument((doc) => ({ ...doc, pages: doc.pages.map((p, i) => i === pageIndex ? { ...p, ...patch } : p), navigation: patch.title === undefined ? doc.navigation : doc.navigation.map((item) => item.pageId === doc.pages[pageIndex]?.id ? { ...item, label: patch.title! } : item) }));
  }
  function updateSection(patch: Partial<SectionDoc>) {
    updateDocument((doc) => ({ ...doc, pages: doc.pages.map((p, pi) => pi !== pageIndex ? p : { ...p, sections: p.sections.map((s, si) => si === sectionIndex ? { ...s, ...patch } : s) }) }));
  }
  function syncEditorText(nextPageIndex: number, nextSectionIndex: number) {
    const p = document?.pages?.[nextPageIndex];
    setPropsText(JSON.stringify(p?.sections?.[nextSectionIndex]?.props ?? {}, null, 2));
    setStyleText(JSON.stringify(p?.sections?.[nextSectionIndex]?.style ?? {}, null, 2));
  }

  function moveSection(direction: -1 | 1) {
    if (!page || sectionIndex + direction < 0 || sectionIndex + direction >= page.sections.length) return;
    const nextIndex = sectionIndex + direction;
    updateDocument((doc) => ({ ...doc, pages: doc.pages.map((p, pi) => {
      if (pi !== pageIndex) return p;
      const sections = reorderItems(p.sections, sectionIndex, nextIndex);
      return { ...p, sections };
    }) }));
    setSectionIndex(nextIndex);
    setPropsText(JSON.stringify(page.sections[nextIndex]?.props ?? {}, null, 2));
    setStyleText(JSON.stringify(page.sections[nextIndex]?.style ?? {}, null, 2));
  }
  function addPresetSection() {
    if (!page || !selectedPreset) return;
    const nextSection: SectionDoc = {
      id: crypto.randomUUID(), type: selectedPreset.sectionType, variant: selectedPreset.variant, visible: true,
      props: (selectedPreset.props && typeof selectedPreset.props === 'object' ? structuredClone(selectedPreset.props) : {}) as Record<string, unknown>,
      style: (selectedPreset.style && typeof selectedPreset.style === 'object' ? structuredClone(selectedPreset.style) : { align: 'left', spacing: 'comfortable' }) as Record<string, unknown>,
    };
    const nextIndex = page.sections.length;
    updateDocument((doc) => ({ ...doc, pages: doc.pages.map((p, pi) => pi === pageIndex ? { ...p, sections: [...p.sections, nextSection] } : p) }));
    setSectionIndex(nextIndex); setPropsText(JSON.stringify(nextSection.props, null, 2)); setStyleText(JSON.stringify(nextSection.style ?? {}, null, 2));
    setMessage('Preset section added locally. Save draft to persist it.'); setError('');
  }
  function removeSection() {
    if (!page || !section || page.sections.length <= 1 || !window.confirm('Remove this section from the draft? Save draft to persist the change.')) return;
    const nextSections = page.sections.filter((_, i) => i !== sectionIndex);
    const nextIndex = Math.max(0, Math.min(sectionIndex, nextSections.length - 1));
    updateDocument((doc) => ({ ...doc, pages: doc.pages.map((p, pi) => pi === pageIndex ? { ...p, sections: nextSections } : p) }));
    setSectionIndex(nextIndex); setPropsText(JSON.stringify(nextSections[nextIndex]?.props ?? {}, null, 2)); setStyleText(JSON.stringify(nextSections[nextIndex]?.style ?? {}, null, 2));
    setMessage('Section removed locally. Save draft to persist the change.'); setError('');
  }

  function addCustomPage() {
    if (!draft || !document || !catalog.capabilities.customPages) return;
    if (catalog.capabilities.maxPages !== null && document.pages.length >= catalog.capabilities.maxPages) {
      setError(`Your plan allows up to ${catalog.capabilities.maxPages} pages per storefront.`); return;
    }
    const preset = selectedPreset ?? catalog.presets[0];
    if (!preset) { setError('Add a section preset before creating a page.'); return; }
    const id = crypto.randomUUID();
    const page: PageDoc = { id, kind: 'custom', slug: `page-${id.slice(0, 8)}`, title: 'New page', visible: true, seo: { title: '', description: '' }, sections: [{ id: crypto.randomUUID(), type: preset.sectionType, variant: preset.variant, visible: true, props: (structuredClone(preset.props ?? {}) as Record<string, unknown>), style: (structuredClone(preset.style ?? { align: 'left', spacing: 'comfortable' }) as Record<string, unknown>) }] };
    updateDocument((doc) => ({ ...doc, pages: [...doc.pages, page], navigation: [...doc.navigation, { label: page.title, pageId: id, visible: true }] }));
    const index = document.pages.length;
    setPageIndex(index); setSectionIndex(0); setPropsText(JSON.stringify(page.sections[0]?.props ?? {}, null, 2)); setStyleText(JSON.stringify(page.sections[0]?.style ?? {}, null, 2));
    setMessage('Custom page added locally. Save draft to persist it.'); setError('');
  }
  function removeCustomPage() {
    if (!page || page.kind !== 'custom' || !document || document.pages.length <= 1 || !window.confirm(`Remove the draft page “${page.title}”?`)) return;
    const removedId = page.id;
    const nextPages = document.pages.filter((p) => p.id !== removedId);
    const nextIndex = Math.max(0, Math.min(pageIndex - 1, nextPages.length - 1));
    updateDocument((doc) => ({ ...doc, pages: doc.pages.filter((p) => p.id !== removedId), navigation: doc.navigation.filter((n) => n.pageId !== removedId) }));
    setPageIndex(nextIndex); setSectionIndex(0); setPropsText(JSON.stringify(nextPages[nextIndex]?.sections?.[0]?.props ?? {}, null, 2)); setStyleText(JSON.stringify(nextPages[nextIndex]?.sections?.[0]?.style ?? {}, null, 2));
    setMessage('Custom page removed locally. Save draft to persist it.'); setError('');
  }
  function saveSectionStyle() {
    try {
      const parsed = JSON.parse(styleText) as Record<string, unknown>;
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('Style must be an object.');
      updateSection({ style: parsed }); setMessage('Section style applied locally. Save draft to persist it.'); setError('');
    } catch { setError('Section style must be a valid JSON object. Nothing was applied.'); }
  }

  async function previewTemplateSwitch() {
    if (!details || !draft || !targetTemplate || targetTemplate === details.templateKey) return;
    setBusy(true); setError(''); setMessage(''); setSwitchPlan(null);
    try { setSwitchPlan(await api<SwitchPlan>(`/api/storefronts/${details.id}/template-switch?target=${encodeURIComponent(targetTemplate)}`)); }
    catch (e) { setError(messageFor(e)); }
    finally { setBusy(false); }
  }
  async function applyTemplateSwitch() {
    if (!details || !draft || !switchPlan) return;
    if (!window.confirm(`Switch this draft from ${switchPlan.fromTemplate} to ${switchPlan.toTemplate}? The preview lists sections that will be converted or archived.`)) return;
    setBusy(true); setError(''); setMessage('');
    try {
      const saved = await api<Draft>(`/api/storefronts/${details.id}/template-switch`, { method: 'POST', json: { targetTemplate: switchPlan.toTemplate, token: switchPlan.token, useTemplateTheme: false, expectedRevision: draft.revision } });
      setDraft(saved); setSwitchPlan(null); setTargetTemplate(switchPlan.toTemplate); setThemeText(JSON.stringify((saved.document as DocumentDoc).theme ?? {}, null, 2));
      const refreshed = await api<SiteDetails>(`/api/storefronts/${details.id}`); setDetails(refreshed);
      setSites((old) => old.map((site) => site.id === refreshed.id ? { ...site, templateKey: refreshed.templateKey, draftRevision: saved.revision, hasUnpublishedChanges: true } : site));
      setMessage('Template switched in the draft. Save/publish after reviewing the content.');
    } catch (e) { setError(messageFor(e)); }
    finally { setBusy(false); }
  }
  async function addCustomDomain(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!details) return;
    setBusy(true); setError(''); setMessage('');
    try { const added = await api<CustomDomain>(`/api/storefronts/${details.id}/domains`, { method: 'POST', json: { hostname: newDomain.trim() } }); setDomains((old) => [...old.filter((x) => x.id !== added.id), added]); setNewDomain(''); setMessage('Domain added. Create the exact DNS TXT record shown below, then verify it.'); }
    catch (e) { setError(messageFor(e)); }
    finally { setBusy(false); }
  }
  async function verifyCustomDomain(domain: CustomDomain) {
    if (!details) return; setBusy(true); setError(''); setMessage('');
    try { const updated = await api<CustomDomain>(`/api/storefronts/${details.id}/domains/${domain.id}/verify`, { method: 'POST' }); setDomains((old) => old.map((x) => x.id === updated.id ? updated : x)); setMessage(updated.status === 'Verified' ? `${updated.hostname} verified.` : `Verification status: ${updated.status}. Check the DNS TXT record and try again.`); }
    catch (e) { setError(messageFor(e)); }
    finally { setBusy(false); }
  }
  async function removeCustomDomain(domain: CustomDomain) {
    if (!details || !window.confirm(`Remove ${domain.hostname} from this storefront?`)) return;
    setBusy(true); setError(''); setMessage('');
    try { await api(`/api/storefronts/${details.id}/domains/${domain.id}`, { method: 'DELETE' }); setDomains((old) => old.filter((x) => x.id !== domain.id)); setMessage('Custom domain removed.'); }
    catch (e) { setError(messageFor(e)); }
    finally { setBusy(false); }
  }

  async function uploadMedia(event: ChangeEvent<HTMLInputElement>) {
    const file = event.currentTarget.files?.[0];
    // Reset the control so the same file can be selected again after a failed upload.
    event.currentTarget.value = '';
    if (!file) return;
    if (!file.type.startsWith('image/')) { setError('Choose an image file.'); return; }
    if (file.size > 12 * 1024 * 1024) { setError('Choose an image smaller than 12 MB.'); return; }
    setBusy(true); setError(''); setMessage('');
    try {
      const uploaded = await apiUpload<MediaAsset>('/api/media', file, { purpose: 'DesignImage' });
      setMediaAssets((old) => [uploaded, ...old.filter((asset) => asset.id !== uploaded.id)]);
      setSelectedMediaId(uploaded.id);
      setMessage(`${uploaded.fileName} uploaded and selected. Choose “Use for selected section” or “Use as logo”, then save the draft.`);
    } catch (e) { setError(messageFor(e)); }
    finally { setBusy(false); }
  }

  function useMediaForSection() {
    if (!section || !selectedMediaId || !sectionSupportsMedia) return;
    const selected = mediaAssets.find((asset) => asset.id === selectedMediaId);
    if (!selected) return;
    if (section.type === 'gallery') {
      const current = Array.isArray(section.props.images) ? section.props.images as Record<string, unknown>[] : [];
      updateSection({ props: { ...section.props, images: [...current, { mediaId: selected.id, alt: selected.fileName, caption: '' }] } });
      setPropsText(JSON.stringify({ ...section.props, images: [...current, { mediaId: selected.id, alt: selected.fileName, caption: '' }] }, null, 2));
    } else {
      const nextProps = { ...section.props, imageMediaId: selected.id };
      updateSection({ props: nextProps }); setPropsText(JSON.stringify(nextProps, null, 2));
    }
    setMessage(`Selected ${selected.fileName} for this section. Save draft to persist it.`); setError('');
  }
  function useMediaAsLogo() {
    if (!selectedMediaId || !document) return;
    const selected = mediaAssets.find((asset) => asset.id === selectedMediaId);
    if (!selected) return;
    updateDocument((doc) => ({ ...doc, branding: { ...doc.branding, logoMediaId: selected.id } }));
    setMessage(`Selected ${selected.fileName} as the storefront logo. Save draft to persist it.`); setError('');
  }

  async function createSite() {
    setBusy(true); setError(''); setMessage('');
    try {
      const created = await api<SiteDetails>('/api/storefronts', { method: 'POST', json: { name: newName.trim(), templateKey: newTemplate } });
      const next: SiteSummary = { id: created.id, name: created.name, slug: created.slug, status: created.status, templateKey: created.templateKey, publishedVersion: created.publishedVersion, draftRevision: created.draftRevision, hasUnpublishedChanges: created.hasUnpublishedChanges, updatedAtUtc: new Date().toISOString() };
      setSites((old) => [...old, next]); setNewName(''); setSelectedId(created.id); setMessage('Storefront created. It is still a draft until you publish it.');
    } catch (e) { setError(messageFor(e)); }
    finally { setBusy(false); }
  }
  async function saveDraft() {
    if (!details || !draft || !document) return;
    setBusy(true); setError(''); setMessage('');
    try {
      const saved = await api<Draft>(`/api/storefronts/${details.id}/draft`, { method: 'PUT', json: { expectedRevision: draft.revision, document } });
      setDraft(saved); setThemeText(JSON.stringify((saved.document as DocumentDoc).theme ?? {}, null, 2));
      const refreshedDetails = await api<SiteDetails>(`/api/storefronts/${details.id}`); setDetails(refreshedDetails);
      setSites((old) => old.map((s) => s.id === details.id ? { ...s, draftRevision: saved.revision, hasUnpublishedChanges: true } : s));
      setMessage('Draft saved. Visitors still see the published version until you publish.');
    } catch (e) { setError(messageFor(e)); }
    finally { setBusy(false); }
  }
  async function saveProps() {
    try {
      const parsed = JSON.parse(propsText) as Record<string, unknown>;
      updateSection({ props: parsed }); setMessage('Section content applied locally. Save draft to persist it.'); setError('');
    } catch { setError('Section content must be valid JSON. Nothing was applied.'); }
  }
  async function applyTheme() {
    try {
      const parsed = JSON.parse(themeText) as unknown;
      updateDocument((doc) => ({ ...doc, theme: parsed })); setMessage('Theme applied locally. Save draft to persist it.'); setError('');
    } catch { setError('Theme must be valid JSON. Nothing was applied.'); }
  }
  async function saveAppearance() {
    if (!details || !draft || !document) return;
    setBusy(true); setError(''); setMessage('');
    try {
      const saved = await api<Draft>(`/api/storefronts/${details.id}/appearance`, { method: 'PUT', json: { expectedRevision: draft.revision, theme: document.theme, branding: document.branding } });
      setDraft(saved); setThemeText(JSON.stringify((saved.document as DocumentDoc).theme ?? {}, null, 2));
      setDetails(await api<SiteDetails>(`/api/storefronts/${details.id}`));
      setMessage('Appearance saved. Publish the storefront to make the change public.');
    } catch (e) { setError(messageFor(e)); }
    finally { setBusy(false); }
  }

  async function updateSettings() {
    if (!details) return;
    setBusy(true); setError(''); setMessage('');
    try {
      const updated = await api<SiteDetails>(`/api/storefronts/${details.id}/settings`, { method: 'PUT', json: { version: details.version, name: settingsName.trim(), contactEmail: contactEmail.trim() || null, contactPhone: contactPhone.trim() || null, address: address.trim() || null, openingHours: openingHours.trim() || null, workflowInquiry: details.workflowInquiry, workflowBooking: details.workflowBooking, workflowCustomOrder: details.workflowCustomOrder, workflowContact: details.workflowContact, allowSearchIndexing: details.allowSearchIndexing } });
      setDetails(updated); setSites((old) => old.map((s) => s.id === updated.id ? { ...s, name: updated.name } : s)); setMessage('Storefront settings saved.');
    } catch (e) { setError(messageFor(e)); }
    finally { setBusy(false); }
  }
  async function publish() {
    if (!details) return;
    if (!window.confirm('Publish this saved draft to the public storefront?')) return;
    setBusy(true); setError(''); setMessage('');
    try {
      const result = await api<{ version: Version }>(`/api/storefronts/${details.id}/publish`, { method: 'POST', json: { note: note.trim() || null } });
      setMessage(`Published version ${result.version.versionNumber}.`); setNote(''); await refresh(details.id);
      setSites((old) => old.map((s) => s.id === details.id ? { ...s, status: 'Published', publishedVersion: result.version.versionNumber, hasUnpublishedChanges: false } : s));
    } catch (e) { setError(messageFor(e)); }
    finally { setBusy(false); }
  }
  async function unpublish() {
    if (!details || !window.confirm('Unpublish this storefront? Visitors will no longer see it.')) return;
    setBusy(true); setError(''); setMessage('');
    try { await api(`/api/storefronts/${details.id}/unpublish`, { method: 'POST' }); setMessage('Storefront unpublished.'); await refresh(details.id); setSites((old) => old.map((s) => s.id === details.id ? { ...s, status: 'Unpublished' } : s)); }
    catch (e) { setError(messageFor(e)); } finally { setBusy(false); }
  }
  async function preview() {
    if (!details) return;
    setBusy(true); setError('');
    try { const result = await api<{ token: string }>(`/api/storefronts/${details.id}/preview-link`, { method: 'POST' }); const url = `/preview/${encodeURIComponent(result.token)}`; const opened = window.open(url, '_blank', 'noopener,noreferrer'); if (!opened) window.location.assign(url); }
    catch (e) { setError(messageFor(e)); } finally { setBusy(false); }
  }
  async function rollback(versionNumber: number) {
    if (!details || !window.confirm(`Restore version ${versionNumber}? This creates a new published version; existing history is retained.`)) return;
    setBusy(true); setError(''); setMessage('');
    try { const result = await api<{ version: Version }>(`/api/storefronts/${details.id}/versions/${versionNumber}/rollback`, { method: 'POST', json: { note: `Rollback from version ${versionNumber}` } }); setMessage(`Rollback published as version ${result.version.versionNumber}.`); await refresh(details.id); }
    catch (e) { setError(messageFor(e)); } finally { setBusy(false); }
  }

  return <div className="ap-stack">
    <div className="ap-row"><div><h1 style={{ marginBottom: '.25rem' }}>Storefront builder</h1><p className="ap-foot" style={{ margin: 0 }}>Edit the draft, preview privately, then publish. Visitors never see unsaved draft changes.</p></div></div>
    {error && <p className="ap-alert" role="alert">{error}</p>}{message && <p className="ap-ok" role="status">{message}</p>}
    <section className="ap-panel ap-stack">
      <h2 className="ap-h2">Your storefronts</h2>
      <div className="ap-filter"><label htmlFor="site-select">Choose storefront</label><select id="site-select" className="ap-input" value={selectedId} onChange={(e) => setSelectedId(e.target.value)}><option value="">Select a storefront</option>{sites.map((s) => <option key={s.id} value={s.id}>{s.name} · {s.slug} · {s.status}</option>)}</select></div>
      <p className="ap-foot">Plan allowance: {catalog.capabilities.maxStorefronts ?? 'Unlimited'} storefront(s); template tier {catalog.capabilities.templateTier}. {catalog.capabilities.builder ? 'Full builder enabled.' : `Builder tier: ${catalog.capabilities.sectionLevel}; your plan may restrict sections.`}</p>
      {canEdit && <form className="ap-filter" onSubmit={(e) => { e.preventDefault(); void createSite(); }}><label className="ap-sr" htmlFor="new-site-name">Storefront name</label><input id="new-site-name" className="ap-input" placeholder="New storefront name" value={newName} maxLength={80} minLength={2} required onChange={(e) => setNewName(e.target.value)} /><label className="ap-sr" htmlFor="new-site-template">Starting template</label><select id="new-site-template" className="ap-input" value={newTemplate} onChange={(e) => setNewTemplate(e.target.value)}>{catalog.templates.map((t) => <option key={t.key} value={t.key} disabled={!t.allowed}>{t.name}{!t.allowed ? ` · requires ${t.tier}` : ''}</option>)}</select><button className="ap-btn" disabled={busy || !newName.trim()}>Create storefront</button></form>}
    </section>
    {details && draft && document && <>
      <section className="ap-panel ap-stack">
        <div className="ap-row"><div><h2 className="ap-h2">{details.name}</h2><p className="ap-foot" style={{ margin: 0 }}>/{details.slug} · {details.status} · draft revision {draft.revision}{details.publishedVersion ? ` · live version ${details.publishedVersion}` : ' · not published'}</p></div><div className="ap-actions"><button className="ap-btn ap-btn-quiet" disabled={busy} onClick={() => void preview()}>Preview draft</button>{canEdit && <button className="ap-btn" disabled={busy || !catalog.capabilities.builder} onClick={() => void saveDraft()}>Save draft</button>}</div></div>
        {draft.planIssues.length > 0 && <div className="ap-alert"><strong>Plan restrictions</strong><ul>{draft.planIssues.map((i, n) => <li key={`${i.code}-${n}`}>{i.message}</li>)}</ul></div>}
        {draft.contrastWarnings.length > 0 && <div className="ap-alert"><strong>Contrast warnings</strong><ul>{draft.contrastWarnings.map((w, n) => <li key={n}>{w.foreground} on {w.background}: {w.ratio.toFixed(2)}:1 (recommended {w.required}:1).</li>)}</ul></div>}
        <div className="ap-grid2">
          <div className="ap-field"><label htmlFor="page-select">Page</label><select id="page-select" className="ap-input" value={pageIndex} onChange={(e) => { const i = Number(e.target.value); setPageIndex(i); setSectionIndex(0); syncEditorText(i, 0); }} >{document.pages.map((p, i) => <option key={p.id} value={i}>{p.title || 'Home'} · {p.kind}{p.visible ? '' : ' · hidden'}</option>)}</select></div>
          <div className="ap-field"><label htmlFor="section-select">Section</label><select id="section-select" className="ap-input" value={sectionIndex} onChange={(e) => { const i = Number(e.target.value); setSectionIndex(i); syncEditorText(pageIndex, i); }}>{(page?.sections ?? []).map((s, i) => <option key={s.id} value={i}>{i + 1}. {s.type} · {s.variant}{s.visible ? '' : ' · hidden'}</option>)}</select></div>
        </div>
        {page && <fieldset className="ap-fieldset ap-stack"><legend>Page settings</legend><div className="ap-actions"><button type="button" className="ap-btn ap-btn-quiet ap-btn-sm" disabled={!canEdit || !catalog.capabilities.builder || !catalog.capabilities.customPages || (catalog.capabilities.maxPages !== null && document.pages.length >= catalog.capabilities.maxPages)} onClick={addCustomPage}>Add custom page</button><button type="button" className="ap-btn ap-btn-danger ap-btn-sm" disabled={!canEdit || !catalog.capabilities.builder || !catalog.capabilities.customPages || page.kind !== 'custom' || document.pages.length <= 1} onClick={removeCustomPage}>Remove custom page</button></div><div className="ap-field"><label htmlFor="page-title">Page title</label><input id="page-title" className="ap-input" maxLength={100} value={page.title} disabled={!canEdit || !catalog.capabilities.builder} onChange={(e) => updatePage({ title: e.target.value })} /></div><div className="ap-field"><label htmlFor="page-slug">URL slug</label><input id="page-slug" className="ap-input" maxLength={80} value={page.slug} disabled={!canEdit || !catalog.capabilities.builder || page.kind === 'home'} onChange={(e) => updatePage({ slug: e.target.value })} /><p className="ap-foot">Use a lowercase URL segment. The API validates the slug when saving.</p></div><div className="ap-grid2"><div className="ap-field"><label htmlFor="page-seo-title">SEO title</label><input id="page-seo-title" className="ap-input" maxLength={120} value={page.seo?.title ?? ''} disabled={!canEdit || !catalog.capabilities.builder} onChange={(e) => updatePage({ seo: { ...(page.seo ?? {}), title: e.target.value } })} /></div><div className="ap-field"><label htmlFor="page-seo-description">SEO description</label><textarea id="page-seo-description" className="ap-input" rows={3} maxLength={300} value={page.seo?.description ?? ''} disabled={!canEdit || !catalog.capabilities.builder} onChange={(e) => updatePage({ seo: { ...(page.seo ?? {}), description: e.target.value } })} /></div></div><label className="ap-check"><input type="checkbox" checked={page.visible} disabled={!canEdit || !catalog.capabilities.builder} onChange={(e) => updatePage({ visible: e.target.checked })} /> Show this page to visitors</label><div className="ap-field"><strong>Navigation</strong>{document.navigation.filter((n) => n.pageId === page.id).map((item) => <label className="ap-check" key={item.pageId}><input type="checkbox" checked={item.visible} disabled={!canEdit || !catalog.capabilities.builder} onChange={(e) => updateDocument((doc) => ({ ...doc, navigation: doc.navigation.map((n) => n.pageId === item.pageId ? { ...n, visible: e.target.checked } : n) }))} /> Show this page in site navigation</label>)}{!document.navigation.some((n) => n.pageId === page.id) && <button type="button" className="ap-btn ap-btn-quiet ap-btn-sm" disabled={!canEdit || !catalog.capabilities.builder} onClick={() => updateDocument((doc) => ({ ...doc, navigation: [...doc.navigation, { pageId: page.id, label: page.title || 'Page', visible: true }] }))}>Add page to navigation</button>}</div></fieldset>}
        {page && page.sections.length > 1 && <fieldset className="ap-fieldset ap-stack"><legend>Reorder sections</legend><p className="ap-foot" style={{ margin: 0 }}>Drag a section onto another section to change its order, or select it to edit. Changes stay in the draft until saved.</p><div className="ap-stack" aria-label="Draggable storefront sections">{page.sections.map((item, index) => <button key={item.id} type="button" draggable={canEdit && catalog.capabilities.builder} className={`ap-btn ${index === sectionIndex ? '' : 'ap-btn-quiet'}`} aria-pressed={index === sectionIndex} disabled={!canEdit || !catalog.capabilities.builder} onClick={() => { setSectionIndex(index); syncEditorText(pageIndex, index); }} onDragStart={(event) => { setDraggedSectionIndex(index); event.dataTransfer.effectAllowed = 'move'; event.dataTransfer.setData('text/plain', String(index)); }} onDragOver={(event) => { if (draggedSectionIndex !== null) { event.preventDefault(); event.dataTransfer.dropEffect = 'move'; } }} onDrop={(event) => { event.preventDefault(); const from = draggedSectionIndex ?? Number(event.dataTransfer.getData('text/plain')); if (!Number.isInteger(from) || from < 0 || from >= page.sections.length || from === index) { setDraggedSectionIndex(null); return; } const reordered = reorderItems(page.sections, from, index); updateDocument((doc) => ({ ...doc, pages: doc.pages.map((p, pi) => pi === pageIndex ? { ...p, sections: reordered } : p) })); setSectionIndex(index); setPropsText(JSON.stringify(reordered[index]?.props ?? {}, null, 2)); setStyleText(JSON.stringify(reordered[index]?.style ?? {}, null, 2)); setDraggedSectionIndex(null); setMessage('Section order changed in the draft. Save draft to persist it.'); setError(''); }} onDragEnd={() => setDraggedSectionIndex(null)}>{index + 1}. {item.type} · {item.variant}{item.visible ? '' : ' · hidden'}</button>)}</div></fieldset>}
        {section && <fieldset className="ap-fieldset ap-stack"><legend>Section settings</legend><p className="ap-foot" style={{ margin: 0 }}>Section type: <strong>{section.type}</strong>. Available variants are defined by the template and plan.</p><div className="ap-actions"><button type="button" className="ap-btn ap-btn-quiet ap-btn-sm" disabled={!canEdit || !catalog.capabilities.builder || sectionIndex === 0} onClick={() => moveSection(-1)}>Move up</button><button type="button" className="ap-btn ap-btn-quiet ap-btn-sm" disabled={!canEdit || !catalog.capabilities.builder || sectionIndex >= page.sections.length - 1} onClick={() => moveSection(1)}>Move down</button><button type="button" className="ap-btn ap-btn-danger ap-btn-sm" disabled={!canEdit || !catalog.capabilities.builder || page.sections.length <= 1} onClick={removeSection}>Remove section</button></div><div className="ap-field"><label htmlFor="section-variant">Layout variant</label><select id="section-variant" className="ap-input" value={section.variant} disabled={!canEdit || !catalog.capabilities.builder} onChange={(e) => updateSection({ variant: e.target.value })}>{(currentTemplate?.supported?.[section.type] ?? [section.variant]).map((v) => <option key={v} value={v}>{v}</option>)}</select></div><label className="ap-check"><input type="checkbox" checked={section.visible} disabled={!canEdit || !catalog.capabilities.builder} onChange={(e) => updateSection({ visible: e.target.checked })} /> Show this section</label><div className="ap-field"><label htmlFor="section-props">Section content (JSON)</label><textarea id="section-props" className="ap-input ap-codearea" rows={10} spellCheck={false} value={propsText} disabled={!canEdit || !catalog.capabilities.builder} onChange={(e) => setPropsText(e.target.value)} /><p className="ap-foot">Edit text and content fields without changing the section ID or its page. Invalid JSON is not applied.</p><button type="button" className="ap-btn ap-btn-quiet" disabled={!canEdit || !catalog.capabilities.builder} onClick={() => void saveProps()}>Apply section content</button></div><div className="ap-field"><label htmlFor="section-style">Section appearance (JSON)</label><textarea id="section-style" className="ap-input ap-codearea" rows={5} spellCheck={false} value={styleText} disabled={!canEdit || !catalog.capabilities.builder} onChange={(e) => setStyleText(e.target.value)} /><p className="ap-foot">Edit the validated section style object, including alignment, spacing, background, text colour, and button shape.</p><button type="button" className="ap-btn ap-btn-quiet" disabled={!canEdit || !catalog.capabilities.builder} onClick={saveSectionStyle}>Apply section appearance</button></div><div className="ap-field"><label htmlFor="section-preset">Add a section from preset</label><div className="ap-filter"><select id="section-preset" className="ap-input" value={presetKey || selectedPreset?.key || ''} disabled={!canEdit || !catalog.capabilities.builder || catalog.presets.length === 0} onChange={(e) => setPresetKey(e.target.value)}>{catalog.presets.map((p) => <option key={p.key} value={p.key}>{p.name} · {p.sectionType}/{p.variant}</option>)}</select><button type="button" className="ap-btn ap-btn-quiet" disabled={!canEdit || !catalog.capabilities.builder || !selectedPreset} onClick={addPresetSection}>Add section</button></div></div></fieldset>}
        <fieldset className="ap-fieldset ap-stack"><legend>Theme configuration</legend><div className="ap-field"><label htmlFor="theme-json">Theme (JSON)</label><textarea id="theme-json" className="ap-input ap-codearea" rows={7} spellCheck={false} value={themeText} disabled={!canEdit || catalog.capabilities.themeLevel === 'none'} onChange={(e) => setThemeText(e.target.value)} /><p className="ap-foot">The API validates allowed fonts, colours, and plan limits when the draft is saved.</p><button type="button" className="ap-btn ap-btn-quiet" disabled={!canEdit || catalog.capabilities.themeLevel === 'none'} onClick={() => void applyTheme()}>Apply theme locally</button>{canEdit && !catalog.capabilities.builder && <button type="button" className="ap-btn" disabled={busy || catalog.capabilities.themeLevel === 'none'} onClick={() => void saveAppearance()}>Save appearance</button>}</div></fieldset>
        <fieldset className="ap-fieldset ap-stack"><legend>Template</legend><p className="ap-foot" style={{ margin: 0 }}>Preview the changes before switching. Unsupported sections are archived by the API rather than silently discarded.</p><div className="ap-filter"><select className="ap-input" aria-label="Target storefront template" value={targetTemplate || details.templateKey} disabled={!canEdit || busy} onChange={(e) => { setTargetTemplate(e.target.value); setSwitchPlan(null); }}>{catalog.templates.map((t) => <option key={t.key} value={t.key} disabled={!t.allowed}>{t.name}{!t.allowed ? ` · requires ${t.tier}` : ''}</option>)}</select><button type="button" className="ap-btn ap-btn-quiet" disabled={!canEdit || !catalog.capabilities.builder || busy || !targetTemplate || targetTemplate === details.templateKey} onClick={() => void previewTemplateSwitch()}>Preview template switch</button></div>{switchPlan && <div className="ap-stack"><p>Switch preview: {switchPlan.kept.length} kept, {switchPlan.converted.length} converted, {switchPlan.archived.length} archived, {switchPlan.restored.length} restored.</p>{switchPlan.archived.length > 0 && <ul>{switchPlan.archived.map((x) => <li key={x.sectionId}>{x.page}: {x.type} ({x.fromVariant ?? 'default'}) will be archived.</li>)}</ul>}<div className="ap-actions"><button type="button" className="ap-btn" disabled={busy || !canEdit} onClick={() => void applyTemplateSwitch()}>Apply template switch</button><button type="button" className="ap-btn ap-btn-quiet" disabled={busy} onClick={() => setSwitchPlan(null)}>Cancel preview</button></div></div>}</fieldset>
        {canPublish && <fieldset className="ap-fieldset ap-stack"><legend>Publish and version history</legend><div className="ap-field"><label htmlFor="publish-note">Version note (optional)</label><input id="publish-note" className="ap-input" maxLength={200} value={note} onChange={(e) => setNote(e.target.value)} placeholder="e.g. Spring collection refresh" /></div><div className="ap-actions"><button className="ap-btn" disabled={busy || draft.planIssues.length > 0} onClick={() => void publish()}>Publish saved draft</button>{details.status === 'Published' && <button className="ap-btn ap-btn-danger" disabled={busy} onClick={() => void unpublish()}>Unpublish</button>}</div><div className="ap-scroll"><table className="ap-table"><thead><tr><th>Version</th><th>Template</th><th>Published</th><th>Status</th><th>Action</th></tr></thead><tbody>{versions.map((v) => <tr key={v.versionNumber}><td>v{v.versionNumber}</td><td>{v.templateKey}</td><td>{new Date(v.publishedAtUtc).toLocaleString()}</td><td>{v.isLive ? 'Live' : v.rolledBackFromVersion ? `Rollback from v${v.rolledBackFromVersion}` : v.note || 'Archived'}</td><td>{!v.isLive && <button className="ap-btn ap-btn-quiet ap-btn-sm" disabled={busy} onClick={() => void rollback(v.versionNumber)}>Restore</button>}</td></tr>)}</tbody></table>{versions.length === 0 && <p className="ap-foot">No published versions yet.</p>}</div></fieldset>}
      </section>
      <section className="ap-panel ap-stack"><h2 className="ap-h2">Business and contact settings</h2><div className="ap-grid2"><div className="ap-field"><label htmlFor="site-settings-name">Storefront name</label><input id="site-settings-name" className="ap-input" value={settingsName} maxLength={80} disabled={!canEdit} onChange={(e) => setSettingsName(e.target.value)} /></div><div className="ap-field"><label htmlFor="site-email">Contact email</label><input id="site-email" type="email" className="ap-input" value={contactEmail} maxLength={120} disabled={!canEdit} onChange={(e) => setContactEmail(e.target.value)} /></div><div className="ap-field"><label htmlFor="site-phone">Contact phone</label><input id="site-phone" className="ap-input" value={contactPhone} maxLength={40} disabled={!canEdit} onChange={(e) => setContactPhone(e.target.value)} /></div><div className="ap-field"><label htmlFor="site-address">Address</label><input id="site-address" className="ap-input" value={address} maxLength={300} disabled={!canEdit} onChange={(e) => setAddress(e.target.value)} /></div><div className="ap-field"><label htmlFor="site-hours">Opening hours</label><input id="site-hours" className="ap-input" value={openingHours} maxLength={300} disabled={!canEdit} onChange={(e) => setOpeningHours(e.target.value)} /></div></div><button className="ap-btn" disabled={busy || !canEdit || !settingsName.trim()} onClick={() => void updateSettings()}>Save settings</button></section>
      {canViewMedia && <section className="ap-panel ap-stack"><h2 className="ap-h2">Media picker</h2><p className="ap-foot" style={{ margin: 0 }}>Upload an image directly or choose an existing image belonging to this business. Uploading creates a media asset; applying it changes only the local storefront draft until you save.</p><div className="ap-filter"><label className="ap-label" htmlFor="storefront-media-upload">Upload image</label><input id="storefront-media-upload" className="ap-input" type="file" accept="image/*" disabled={!canManageMedia || busy} onChange={(event) => void uploadMedia(event)} /><span className="ap-foot">Requires media-management permission. Images only, maximum 12 MB. The API inspects file contents and may reject unsupported formats.</span></div><div className="ap-filter"><label className="ap-sr" htmlFor="storefront-media-select">Choose media</label><select id="storefront-media-select" className="ap-input" value={selectedMediaId} disabled={busy || mediaAssets.length === 0} onChange={(e) => setSelectedMediaId(e.target.value)}><option value="">Select an image</option>{mediaAssets.map((asset) => <option key={asset.id} value={asset.id}>{asset.fileName} · {asset.purpose} · {asset.width}×{asset.height}</option>)}</select><button type="button" className="ap-btn ap-btn-quiet" disabled={!canEdit || !catalog.capabilities.builder || busy || !selectedMediaId || !sectionSupportsMedia} onClick={useMediaForSection}>Use for selected section</button><button type="button" className="ap-btn ap-btn-quiet" disabled={!canEdit || busy || !selectedMediaId || catalog.capabilities.brandingLevel === 'none'} onClick={useMediaAsLogo}>Use as logo</button></div>{mediaAssets.length === 0 && <p className="ap-foot">No media assets were returned yet. Upload an image above or check your media permissions.</p>}</section>}
      <section className="ap-panel ap-stack"><h2 className="ap-h2">Custom domains</h2><p className="ap-foot" style={{ margin: 0 }}>{catalog.capabilities.customDomain ? 'Add a domain you control. The storefront API requires the verification TXT record before the domain can serve the published site.' : 'Custom domains are not included in this plan. Upgrade the storefront domain entitlement to enable this feature.'}</p>{catalog.capabilities.customDomain && canPublish && <form className="ap-filter" onSubmit={(e) => void addCustomDomain(e)}><label className="ap-sr" htmlFor="new-custom-domain">Domain name</label><input id="new-custom-domain" className="ap-input" value={newDomain} placeholder="shop.example.com" maxLength={253} minLength={4} required disabled={busy} onChange={(e) => setNewDomain(e.target.value)} /><button className="ap-btn" disabled={busy || !newDomain.trim()}>Add domain</button></form>}<div className="ap-scroll"><table className="ap-table"><thead><tr><th>Hostname</th><th>Status</th><th>DNS TXT name</th><th>DNS TXT value</th><th>Action</th></tr></thead><tbody>{domains.map((domain) => <tr key={domain.id}><td>{domain.hostname}</td><td>{domain.status}{domain.lastError && <div className="ap-small ap-dim">{domain.lastError}</div>}</td><td><code>{domain.verificationRecordName}</code></td><td><code>{domain.verificationRecordValue}</code></td><td><div className="ap-actions">{canPublish && domain.status !== 'Verified' && <button type="button" className="ap-btn ap-btn-quiet ap-btn-sm" disabled={busy} onClick={() => void verifyCustomDomain(domain)}>Verify</button>}{canPublish && <button type="button" className="ap-btn ap-btn-danger ap-btn-sm" disabled={busy} onClick={() => void removeCustomDomain(domain)}>Remove</button>}</div></td></tr>)}</tbody></table>{domains.length === 0 && <p className="ap-foot">No custom domains registered.</p>}</div></section>
    </>}
    {!details && sites.length === 0 && <section className="ap-panel"><p style={{ margin: 0 }}>No storefronts yet. Create one above to start with a template.</p></section>}
  </div>;
}
