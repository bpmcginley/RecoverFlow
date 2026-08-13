import * as React from 'react';
import { Box, List, ListItem } from '@stripe/ui-extension-sdk/ui';
import type { CaseSummary } from '../lib/types';
import { formatAmount, formatDate } from '../lib/format';

const reversalLabel = (reason: NonNullable<CaseSummary['reversalReason']>): string =>
  reason === 'refund' ? 'Refunded' : 'Disputed';

// Field rows shared by the customer and invoice views.
export const CaseDetails = ({ caseSummary }: { caseSummary: CaseSummary }) => (
  <Box css={{ stack: 'y' }}>
    <List>
      <ListItem title="Amount" value={formatAmount(caseSummary.amountCents, caseSummary.currency)} />
      <ListItem title="Opened" value={formatDate(caseSummary.openedAt)} />
      {caseSummary.closedAt && (
        <ListItem title="Closed" value={formatDate(caseSummary.closedAt)} />
      )}
      <ListItem title="Retry attempts" value={String(caseSummary.attemptCount)} />
      {caseSummary.reversalReason && (
        <ListItem
          title={reversalLabel(caseSummary.reversalReason)}
          value={formatAmount(caseSummary.reversedCents, caseSummary.currency)}
        />
      )}
    </List>
  </Box>
);
