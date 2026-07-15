import { afterEach, describe, expect, it } from 'vitest';
import i18n, { applyDocumentLanguage } from '../i18n';

afterEach(async () => {
  await i18n.changeLanguage('en');
  applyDocumentLanguage('en');
});

describe('language and direction support', () => {
  it('ships the complete Hindi emergency control copy', async () => {
    await i18n.changeLanguage('hi');

    expect(i18n.t('home.emergency')).toBe('आपातकालीन मदद शुरू करें');
    expect(i18n.t('capture.demo')).toBe('हिंदी डेमो उपयोग करें');
    expect(i18n.t('common.protocolDisclaimer')).toContain('चिकित्सकीय समीक्षा');
  });

  it('uses English fallback for untranslated secondary copy', async () => {
    await i18n.changeLanguage('mr');

    expect(i18n.t('home.emergency')).toBe('आपत्कालीन मदत सुरू करा');
    expect(i18n.t('errors.genericTitle')).toBe('Something went wrong');
  });

  it('sets RTL only for Urdu while preserving the document language', () => {
    applyDocumentLanguage('ur');
    expect(document.documentElement).toHaveAttribute('lang', 'ur');
    expect(document.documentElement).toHaveAttribute('dir', 'rtl');

    applyDocumentLanguage('hi');
    expect(document.documentElement).toHaveAttribute('lang', 'hi');
    expect(document.documentElement).toHaveAttribute('dir', 'ltr');
  });
});
