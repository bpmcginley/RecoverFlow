import * as React from 'react';
import { Badge, Box, Inline, List, ListItem } from '@stripe/ui-extension-sdk/ui';
import type { ExtensionContextValue } from '@stripe/ui-extension-sdk/context';
import { getDunning } from '../lib/api';
import { emailTypeLabel, formatDate } from '../lib/format';
import { useAppData } from '../lib/useAppData';
import type { DunningEntry } from '../lib/types';
import { Loading, LoadFailed } from './States';

const entryStatus = (entry: DunningEntry) => {
  if (entry.resultedInRecovery) return <Badge type="positive">Led to recovery</Badge>;
  if (entry.clickedAt) return <Badge type="info">Clicked</Badge>;
  if (entry.openedAt) return <Badge type="info">Opened</Badge>;
  return <Badge type="neutral">Sent</Badge>;
};

// Recovery email progress for one case, loaded from /app/v1/cases/{id}/dunning.
export const DunningSection = ({
  context,
  caseId,
}: {
  context: ExtensionContextValue;
  caseId: string;
}) => {
  const { loading, data, error, reload } = useAppData(
    () => getDunning(context, caseId),
    [caseId],
  );

  if (loading) return <Loading />;
  if (error) return <LoadFailed error={error} onRetry={reload} />;
  if (!data || data.entries.length === 0) {
    return (
      <Box css={{ color: 'secondary', paddingY: 'small' }}>
        No recovery emails have been sent for this case.
      </Box>
    );
  }

  return (
    <Box css={{ stack: 'y', gap: 'small' }}>
      <Box css={{ font: 'subheading' }}>Recovery emails</Box>
      <List>
        {data.entries.map((entry) => (
          <ListItem
            key={entry.sequenceStep}
            title={
              <Inline>
                Step {entry.sequenceStep}: {emailTypeLabel(entry.emailType)}
              </Inline>
            }
            secondaryTitle={<Inline>Sent {formatDate(entry.sentAt)}</Inline>}
            value={entryStatus(entry)}
          />
        ))}
      </List>
    </Box>
  );
};
