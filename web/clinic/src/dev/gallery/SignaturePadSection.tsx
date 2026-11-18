import { useState } from 'react';
import { useI18n } from '@medvya/i18n';
import { SignaturePad } from '@medvya/ui';

// SignaturePad on /__ui (MDV-P8-019): draw with a finger, stylus or mouse; ink stays black on white in both themes so the form prints as signed.
export default function SignaturePadSection() {
  const { t } = useI18n();
  const [signed, setSigned] = useState(false);
  return (
    <section aria-labelledby="g-signature-pad" className="flex max-w-xl flex-col gap-3">
      <h2 id="g-signature-pad" className="text-heading">SignaturePad (procedure consent on a tablet)</h2>
      <SignaturePad label={t('dental.consent.signature')} hint={t('dental.consent.signatureHint')} clearLabel={t('dental.consent.clear')} onChange={png => setSigned(!!png)} />
      <p role="status" className="text-caption">{signed ? 'Signed (synthetic)' : 'Not signed yet'}</p>
    </section>
  );
}
