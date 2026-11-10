import type { AssetInfo } from '@cathode/api-client';
import { hasSizes, isHiddenCaptureParam } from './capture';

const asset = (extra: object): AssetInfo => ({
  ref: 'sha256:1',
  name: 'Amp.nam',
  kind: 'nam',
  size: 1,
  added: '',
  favorite: false,
  ...extra
});

describe('capture sizes', () => {
  it('has sizes only when the Server says the file is slimmable', () => {
    expect(hasSizes(asset({ slimmable: true }))).toBe(true);
    expect(hasSizes(asset({ slimmable: false }))).toBe(false);
    expect(hasSizes(asset({}))).toBe(false);
    expect(hasSizes(undefined)).toBe(false);
  });

  it('hides Size, and nothing else, for a capture with one size or no capture', () => {
    expect(isHiddenCaptureParam('size', asset({ slimmable: false }))).toBe(true);
    expect(isHiddenCaptureParam('size', undefined)).toBe(true);
    expect(isHiddenCaptureParam('size', asset({ slimmable: true }))).toBe(false);
    expect(isHiddenCaptureParam('bass', undefined)).toBe(false);
  });
});
