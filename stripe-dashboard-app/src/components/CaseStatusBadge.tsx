import * as React from 'react';
import { Badge } from '@stripe/ui-extension-sdk/ui';
import type { RecoveryStatus } from '../lib/types';
import { statusLabel } from '../lib/format';

const badgeType = (status: RecoveryStatus) => {
  switch (status) {
    case 'ActiveRecovery':
      return 'info' as const;
    case 'Recovered':
      return 'positive' as const;
    case 'Lost':
      return 'negative' as const;
    case 'Cancelled':
      return 'neutral' as const;
  }
};

export const CaseStatusBadge = ({ status }: { status: RecoveryStatus }) => (
  <Badge type={badgeType(status)}>{statusLabel(status)}</Badge>
);
