import * as React from 'react';
import { Box, ContextView, Inline } from '@stripe/ui-extension-sdk/ui';
import type { ExtensionContextValue } from '@stripe/ui-extension-sdk/context';
import { getInvoiceCase, isCaseNotFound } from '../lib/api';
import { useAppData } from '../lib/useAppData';
import { CaseDetails } from '../components/CaseDetails';
import { CaseStatusBadge } from '../components/CaseStatusBadge';
import { DunningSection } from '../components/DunningSection';
import { Loading, LoadFailed } from '../components/States';

// Invoice detail view (stripe.dashboard.invoice.detail): the recovery case for
// the invoice being viewed, if RecoverFlow opened one.
const InvoiceDetail = (context: ExtensionContextValue) => {
  const invoiceId = context.environment.objectContext?.id;
  const { loading, data, error, reload } = useAppData(
    () => getInvoiceCase(context, invoiceId ?? ''),
    [invoiceId],
  );

  return (
    <ContextView
      title="RecoverFlow"
      externalLink={{ label: 'Open RecoverFlow', href: 'https://app.recoverflow.org' }}
    >
      {loading && <Loading />}
      {!loading && error !== undefined && isCaseNotFound(error) && (
        <Box css={{ stack: 'y', gap: 'small', paddingY: 'medium' }}>
          <Box css={{ color: 'secondary' }}>No recovery case for this invoice.</Box>
          <Box css={{ color: 'secondary', font: 'caption' }}>
            RecoverFlow opens a case when an invoice payment fails while your account is
            connected.
          </Box>
        </Box>
      )}
      {!loading && error !== undefined && !isCaseNotFound(error) && (
        <LoadFailed error={error} onRetry={reload} />
      )}
      {!loading && data && (
        <Box css={{ stack: 'y', gap: 'medium' }}>
          <Inline>
            <CaseStatusBadge status={data.status} />
          </Inline>
          {data.customerEmail && (
            <Box css={{ color: 'secondary', font: 'caption' }}>{data.customerEmail}</Box>
          )}
          <CaseDetails caseSummary={data} />
          <DunningSection context={context} caseId={data.id} />
        </Box>
      )}
    </ContextView>
  );
};

export default InvoiceDetail;
