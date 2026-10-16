import { StaffTabs } from '@/components/app/StaffTabs';

export default function StaffLayout({ children }: { children: React.ReactNode }) {
  return (
    <>
      <h1>Staff &amp; roles</h1>
      <StaffTabs />
      {children}
    </>
  );
}
