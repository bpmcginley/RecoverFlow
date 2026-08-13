import * as React from 'react';
import { Badge, Box, Link, List, ListItem, SettingsView } from '@stripe/ui-extension-sdk/ui';
import type { ExtensionContextValue } from '@stripe/ui-extension-sdk/context';
import { getSummary } from '../lib/api';
import { formatPercent, formatUsd } from '../lib/format';
import { useAppData } from '../lib/useAppData';
import { CONNECT_URL, Loading, LoadFailed } from '../components/States';

// Settings view: connection status plus links to RecoverFlow.
const AppSettings = (context: ExtensionContextValue) => {
  // A successful authenticated summary call means this Stripe account resolves
  // to a connected RecoverFlow merchant.
  const { loading, data, error, reload } = useAppData(() => getSummary(context), []);

  return (
    <SettingsView>
      <Box css={{ stack: 'y', gap: 'large', padding: 'large' }}>
        {loading && <Loading />}
        {!loading && error !== undefined && <LoadFailed error={error} onRetry={reload} />}
        {!loading && data && (
          <Box css={{ stack: 'y', gap: 'medium' }}>
            <Box css={{ stack: 'x', gap: 'small', alignY: 'center' }}>
              <Badge type="positive">Connected</Badge>
              <Box>This Stripe account is connected to RecoverFlow.</Box>
            </Box>
            <List>
              <ListItem
                title="Recovered all time (net)"
                value={formatUsd(data.allTime.amountRecoveredCents)}
              />
              <ListItem title="Recovery rate" value={formatPercent(data.allTime.recoveryRate)} />
            </List>
          </Box>
        )}
        <Box css={{ stack: 'y', gap: 'small' }}>
          <Link href="https://app.recoverflow.org" external target="_blank">
            Open the RecoverFlow dashboard
          </Link>
          <Link href={CONNECT_URL} external target="_blank">
            recoverflow.org
          </Link>
        </Box>
      </Box>
    </SettingsView>
  );
};

export default AppSettings;
