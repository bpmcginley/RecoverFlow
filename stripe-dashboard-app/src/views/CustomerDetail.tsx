import * as React from 'react';
import { useState } from 'react';
import {
  Accordion,
  AccordionItem,
  Box,
  ContextView,
  Inline,
} from '@stripe/ui-extension-sdk/ui';
import type { ExtensionContextValue } from '@stripe/ui-extension-sdk/context';
import { getCustomerCases } from '../lib/api';
import { formatAmount } from '../lib/format';
import { useAppData } from '../lib/useAppData';
import type { CaseSummary } from '../lib/types';
import { CaseDetails } from '../components/CaseDetails';
import { CaseStatusBadge } from '../components/CaseStatusBadge';
import { DunningSection } from '../components/DunningSection';
import { Loading, LoadFailed } from '../components/States';

const CaseItem = ({
  context,
  caseSummary,
}: {
  context: ExtensionContextValue;
  caseSummary: CaseSummary;
}) => {
  // Load dunning progress lazily, on first expand, and keep it after collapse.
  const [expanded, setExpanded] = useState(false);
  return (
    <AccordionItem
      // The badge sits in the title, not in `actions`: the drawer is narrow enough that
      // the actions slot clips a status word like "Recovering" against the right edge.
      title={
        <Box css={{ stack: 'x', gap: 'small', alignY: 'center' }}>
          <Inline>{formatAmount(caseSummary.amountCents, caseSummary.currency)}</Inline>
          <CaseStatusBadge status={caseSummary.status} />
        </Box>
      }
      subtitle={<Inline>Invoice {caseSummary.stripeInvoiceId}</Inline>}
      onChange={(isOpen) => {
        if (isOpen) setExpanded(true);
      }}
    >
      <Box css={{ stack: 'y', gap: 'medium' }}>
        <CaseDetails caseSummary={caseSummary} />
        {expanded && <DunningSection context={context} caseId={caseSummary.id} />}
      </Box>
    </AccordionItem>
  );
};

// Customer detail view (stripe.dashboard.customer.detail): recovery cases and
// dunning progress for the customer being viewed.
const CustomerDetail = (context: ExtensionContextValue) => {
  const customerId = context.environment.objectContext?.id;
  const { loading, data, error, reload } = useAppData(
    () => getCustomerCases(context, customerId ?? ''),
    [customerId],
  );

  return (
    <ContextView
      title="RecoverFlow"
      externalLink={{ label: 'Open RecoverFlow', href: 'https://app.recoverflow.org' }}
    >
      {loading && <Loading />}
      {!loading && error !== undefined && <LoadFailed error={error} onRetry={reload} />}
      {!loading && data && data.items.length === 0 && (
        <Box css={{ color: 'secondary', paddingY: 'medium' }}>
          No recovery activity for this customer.
        </Box>
      )}
      {!loading && data && data.items.length > 0 && (
        <Box css={{ stack: 'y', gap: 'medium' }}>
          <Box css={{ color: 'secondary', font: 'caption' }}>
            {data.totalCount === 1
              ? '1 recovery case for this customer.'
              : `${data.totalCount} recovery cases for this customer.`}
          </Box>
          <Accordion>
            {data.items.map((item) => (
              <CaseItem key={item.id} context={context} caseSummary={item} />
            ))}
          </Accordion>
        </Box>
      )}
    </ContextView>
  );
};

export default CustomerDetail;
