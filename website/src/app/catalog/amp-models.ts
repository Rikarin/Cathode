/*
 * What each Circuit Amp model is based on. The engine knows its models only by name, so this is kept by hand from
 * docs/07-amp-catalogue.md; the catalogue page lists any model missing here under "Other", which is the cue to add
 * it. Keyed by the model's name as the Amp block's Type choice spells it.
 */

export const AMP_FAMILIES = [
  { id: 'fender', name: 'Fender and American clean' },
  { id: 'british', name: 'Marshall and British' },
  { id: 'boutique', name: 'Vox, class A and boutique' },
  { id: 'highgain', name: 'Mesa/Boogie and high gain' },
  { id: 'bass', name: 'Bass and solid state' }
] as const;

export type AmpFamily = (typeof AMP_FAMILIES)[number]['id'];

export const AMP_MODELS: Record<string, { family: AmpFamily; basedOn: string }> = {
  // Fender and American clean
  Blackface: { family: 'fender', basedOn: 'Fender blackface (Deluxe and Twin style, generic)' },
  'Deluxe Reverb': { family: 'fender', basedOn: 'Fender Deluxe Reverb AB763' },
  'Twin Reverb': { family: 'fender', basedOn: 'Fender Twin Reverb AB763' },
  'Super Reverb': { family: 'fender', basedOn: 'Fender Super Reverb AB763' },
  'Princeton Reverb': { family: 'fender', basedOn: 'Fender Princeton Reverb AA1164' },
  'Vibroverb AB763': { family: 'fender', basedOn: 'Fender Vibroverb AB763' },
  'Vibrolux Reverb AA964': { family: 'fender', basedOn: 'Fender Vibrolux Reverb AA964' },
  'Bandmaster AB763': { family: 'fender', basedOn: 'Fender Bandmaster AB763' },
  'Tremolux AA763': { family: 'fender', basedOn: 'Fender Tremolux AA763' },
  'Princeton AA964': { family: 'fender', basedOn: 'Fender Princeton AA964' },
  'Bassman 5F6-A': { family: 'fender', basedOn: 'Fender Bassman 5F6-A (tweed)' },
  'Bassman 5F6-A Jumped': { family: 'fender', basedOn: 'Fender Bassman 5F6-A, channels jumpered' },
  'Tweed Deluxe 5E3': { family: 'fender', basedOn: 'Fender Tweed Deluxe 5E3' },
  'Tweed Deluxe 5E3 Jumped': { family: 'fender', basedOn: 'Fender Tweed Deluxe 5E3, inputs jumpered' },
  'Tweed Twin 5F8-A': { family: 'fender', basedOn: 'Fender high-power Tweed Twin 5F8-A' },
  'Tweed Twin 5F8-A Jumped': { family: 'fender', basedOn: 'Fender Tweed Twin 5F8-A, channels jumpered' },
  'Champ 5F1': { family: 'fender', basedOn: 'Fender Champ 5F1 (tweed)' },
  'Princeton 5F2-A': { family: 'fender', basedOn: 'Fender Princeton 5F2-A (tweed)' },
  'Concert 6G12': { family: 'fender', basedOn: 'Fender Concert 6G12 (brownface)' },
  'Super 6G4': { family: 'fender', basedOn: 'Fender Super 6G4 (brownface)' },
  'Deluxe 6G3': { family: 'fender', basedOn: 'Fender Deluxe 6G3 (brownface)' },
  'Vibrolux 6G11': { family: 'fender', basedOn: 'Fender Vibrolux 6G11 (brownface, 1962–63)' },
  'Vibro-King': { family: 'fender', basedOn: 'Fender Vibro-King' },
  'Hot Rod Deluxe': { family: 'fender', basedOn: 'Fender Hot Rod Deluxe III' },
  'Blues Junior': { family: 'fender', basedOn: 'Fender Blues Junior' },
  'Scout GA-17RVT': { family: 'fender', basedOn: 'Gibson Scout GA-17RVT' },

  // Marshall and British
  JTM45: { family: 'british', basedOn: 'Marshall JTM45' },
  'Bluesbreaker 1962': { family: 'british', basedOn: 'Marshall 1962 Bluesbreaker' },
  Plexi: { family: 'british', basedOn: 'Marshall 1959 Super Lead "Plexi" (50 W, generic)' },
  'Plexi 100W': { family: 'british', basedOn: 'Marshall Plexi 100 W, jumpered' },
  '1959SLP': { family: 'british', basedOn: 'Marshall 1959SLP reissue' },
  '1987X': { family: 'british', basedOn: 'Marshall 1987X reissue' },
  '1959T Tremolo': { family: 'british', basedOn: 'Marshall JMP 1959T Super Lead Tremolo' },
  'Major 1967': { family: 'british', basedOn: 'Marshall Major (1967)' },
  '18 W 1974X': { family: 'british', basedOn: 'Marshall 18 W 1974X' },
  'JCM800 2203': { family: 'british', basedOn: 'Marshall JCM800 2203 (100 W)' },
  'JCM800 2204': { family: 'british', basedOn: 'Marshall JCM800 2204 (50 W)' },
  'Silver Jubilee': { family: 'british', basedOn: 'Marshall Silver Jubilee 2555' },
  'JCM900 4100': { family: 'british', basedOn: 'Marshall JCM900 4100 (100 W)' },
  'JCM900 4502': { family: 'british', basedOn: 'Marshall JCM900 4502 (50 W)' },
  DSL100: { family: 'british', basedOn: 'Marshall DSL100' },
  'JVM410H OD': { family: 'british', basedOn: 'Marshall JVM410H, OD channels' },
  'JVM410H Clean/Crunch': { family: 'british', basedOn: 'Marshall JVM410H, Clean and Crunch channels' },
  AFD100: { family: 'british', basedOn: 'Marshall AFD100' },
  'JMP-1': { family: 'british', basedOn: 'Marshall JMP-1 preamp' },
  'BE-100': { family: 'british', basedOn: 'Friedman BE-100, BE channel' },
  'BE-100 HBE': { family: 'british', basedOn: 'Friedman BE-100, HBE channel' },
  'Laney GH100L': { family: 'british', basedOn: 'Laney GH100L' },
  'Hiwatt DR103': { family: 'british', basedOn: 'Hiwatt DR103 Custom 100' },
  'Hiwatt DR504': { family: 'british', basedOn: 'Hiwatt Custom 50 DR504' },
  'Cornford MK50H II': { family: 'british', basedOn: 'Cornford MK50H II' },
  'Tiny Terror': { family: 'british', basedOn: 'Orange Tiny Terror' },

  // Vox, class A and boutique
  AC30: { family: 'boutique', basedOn: 'Vox AC30 Top Boost (and the Normal channel)' },
  AC15: { family: 'boutique', basedOn: 'Vox AC15 (EF86)' },
  'DC-30': { family: 'boutique', basedOn: 'Matchless DC-30' },
  Chieftain: { family: 'boutique', basedOn: 'Matchless Chieftain' },
  'Hot Cat 30': { family: 'boutique', basedOn: 'Bad Cat Hot Cat 30' },
  'ODS 70s': { family: 'boutique', basedOn: 'Dumble Overdrive Special (1970s)' },
  'ODS 97': { family: 'boutique', basedOn: 'Dumble Overdrive Special (later)' },
  Express: { family: 'boutique', basedOn: 'Trainwreck Express' },
  Liverpool: { family: 'boutique', basedOn: 'Trainwreck Liverpool' },

  // Mesa/Boogie and high gain
  'Mark IIC+': { family: 'highgain', basedOn: 'Mesa/Boogie Mark IIC+' },
  'Mark IV': { family: 'highgain', basedOn: 'Mesa/Boogie Mark IV' },
  'Mark V': { family: 'highgain', basedOn: 'Mesa/Boogie Mark V' },
  'Dual Rectifier': { family: 'highgain', basedOn: 'Mesa/Boogie Dual Rectifier (2-channel)' },
  'Dual Rectifier 3-Ch': { family: 'highgain', basedOn: 'Mesa/Boogie Dual Rectifier (3-channel)' },
  'Triple Rectifier': { family: 'highgain', basedOn: 'Mesa/Boogie Triple Rectifier' },
  'Rect-o-Verb': { family: 'highgain', basedOn: 'Mesa/Boogie Rect-o-Verb' },
  'Lone Star': { family: 'highgain', basedOn: 'Mesa/Boogie Lone Star' },
  'Subway Blues': { family: 'highgain', basedOn: 'Mesa/Boogie Subway Blues' },
  'TriAxis Rhythm': { family: 'highgain', basedOn: 'Mesa/Boogie TriAxis preamp, rhythm modes' },
  'TriAxis Lead 1': { family: 'highgain', basedOn: 'Mesa/Boogie TriAxis preamp, Lead 1' },
  'TriAxis Lead 2': { family: 'highgain', basedOn: 'Mesa/Boogie TriAxis preamp, Lead 2' },
  'SLO-100': { family: 'highgain', basedOn: 'Soldano SLO-100' },
  '5150': { family: 'highgain', basedOn: 'Peavey 5150' },
  '5150III': { family: 'highgain', basedOn: 'EVH 5150III' },
  'Legacy VL100': { family: 'highgain', basedOn: 'Carvin Legacy VL100' },
  Powerball: { family: 'highgain', basedOn: 'Engl Powerball' },
  'Savage 120': { family: 'highgain', basedOn: 'Engl Savage 120' },
  'Quick Rod': { family: 'highgain', basedOn: 'Splawn Quickrod' },
  Prometheus: { family: 'highgain', basedOn: 'Lichtlaerm Audio Prometheus, lead channel' },
  'Prometheus Clean': { family: 'highgain', basedOn: 'Lichtlaerm Audio Prometheus, clean channel' },

  // Bass and solid state
  SVT: { family: 'bass', basedOn: 'Ampeg SVT (1970, 6550)' },
  'Portaflex B-15N': { family: 'bass', basedOn: 'Ampeg B-15-N Portaflex (1968)' },
  'Bassman 135': { family: 'bass', basedOn: 'Fender Bassman 135 (bass)' },
  'Bass 400': { family: 'bass', basedOn: 'Mesa/Boogie Bass 400' },
  'JC-120': { family: 'bass', basedOn: 'Roland JC-120 Jazz Chorus' },
  'Valvestate 8080': { family: 'bass', basedOn: 'Marshall Valvestate 8080' }
};
