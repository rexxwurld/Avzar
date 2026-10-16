import { Bricolage_Grotesque } from 'next/font/google';

/** One font for the landing page, sign-in screens and the dashboard. Public storefronts keep their own theme fonts. */
export const lpFont = Bricolage_Grotesque({ subsets: ['latin'], display: 'swap', variable: '--font-lp' });
