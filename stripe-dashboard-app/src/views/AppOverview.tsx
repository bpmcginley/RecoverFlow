import * as React from 'react';
import { Box, ContextView, List, ListItem } from '@stripe/ui-extension-sdk/ui';
import type { ExtensionContextValue } from '@stripe/ui-extension-sdk/context';
import { getSummary } from '../lib/api';
import { formatPercent, formatUsd } from '../lib/format';
import { useAppData } from '../lib/useAppData';
import type { Stats } from '../lib/types';
import { Loading, LoadFailed } from '../components/States';

const StatsSection = ({ title, stats }: { title: string; stats: Stats }) => {
  const active = stats.casesByStatus.ActiveRecovery ?? 0;
  const recovered = stats.casesByStatus.Recovered ?? 0;
  const lost = stats.casesByStatus.Lost ?? 0;
  return (
    <Box css={{ stack: 'y', gap: 'small' }}>
      <Box css={{ font: 'subheading' }}>{title}</Box>
      <List>
        <ListItem title="Recovered (net)" value={formatUsd(stats.amountRecoveredCents)} />
        <ListItem title="At risk" value={formatUsd(stats.amountAtRiskCents)} />
        <ListItem title="Recovery rate" value={formatPercent(stats.recoveryRate)} />
        <ListItem title="Cases in recovery" value={String(active)} />
        <ListItem title="Cases recovered" value={String(recovered)} />
        <ListItem title="Cases lost" value={String(lost)} />
      </List>
    </Box>
  );
};

// Drawer view (stripe.dashboard.drawer.default): merchant recovery summary.
const AppOverview = (context: ExtensionContextValue) => {
  const { loading, data, error, reload } = useAppData(() => getSummary(context), []);

  return (
    <ContextView
      title="Recovery overview"
      externalLink={{ label: 'Open RecoverFlow', href: 'https://app.recoverflow.org' }}
    >
      {loading && <Loading />}
      {!loading && error !== undefined && <LoadFailed error={error} onRetry={reload} />}
      {!loading && data && (
        <Box css={{ stack: 'y', gap: 'large' }}>
          <StatsSection title="Last 30 days" stats={data.last30Days} />
          <StatsSection title="All time" stats={data.allTime} />
          <Box css={{ color: 'secondary', font: 'caption' }}>
            Totals combine all currencies and are shown in USD format. Recovered amounts are
            net of refunds and disputes.
          </Box>
        </Box>
      )}
    </ContextView>
  );
};

export default AppOverview;
