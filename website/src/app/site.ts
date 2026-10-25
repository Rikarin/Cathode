/** The site's own address, without a trailing slash: canonical links, social cards and the sitemap are built on it. */
export const SITE_URL = 'https://cathode.rikarin.io';
export const SITE_NAME = 'Cathode';

/** Where the project lives. Every outbound link on the site comes from here. */
export const REPOSITORY = 'https://github.com/Rikarin/cathode';
export const RELEASES = `${REPOSITORY}/releases`;
export const LICENCE = `${REPOSITORY}/blob/master/LICENSE`;
export const HEADLESS_GUIDE = `${REPOSITORY}/blob/master/packaging/headless/README.md`;

/** The sections of the page, in the order they appear; the header's nav and the footer read them. */
export const SECTIONS = [
  { id: 'amps', label: 'Amps' },
  { id: 'cabs', label: 'Cabs' },
  { id: 'effects', label: 'Effects' },
  { id: 'live', label: 'Live' },
  { id: 'rig', label: 'Headless rig' },
  { id: 'download', label: 'Download' }
];
