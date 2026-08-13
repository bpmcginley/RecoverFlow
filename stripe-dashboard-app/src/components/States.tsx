import * as React from 'react';
import { Banner, Box, Button, Link, Spinner } from '@stripe/ui-extension-sdk/ui';
import { ApiError, isNotConfigured, isNotConnected } from '../lib/api';

export const CONNECT_URL = 'https://recoverflow.org/';

export const Loading = () => (
  <Box css={{ stack: 'x', alignX: 'center', paddingY: 'large' }}>
    <Spinner size="large" />
  </Box>
);

export const NotConnected = () => (
  <Banner
    type="caution"
    title="Not connected to RecoverFlow"
    description="This Stripe account is not connected to RecoverFlow yet, so there is no recovery data to show."
    actions={
      <Link href={CONNECT_URL} type="primary" external target="_blank">
        Connect at recoverflow.org
      </Link>
    }
  />
);

export const NotConfigured = () => (
  <Banner
    type="caution"
    title="Setup in progress"
    description="The RecoverFlow API is not accepting requests from this app yet. Try again later."
  />
);

const errorDescription = (error: unknown): string => {
  if (error instanceof ApiError && error.message) return error.message;
  return 'An unexpected error occurred while loading RecoverFlow data.';
};

export const LoadFailed = ({ error, onRetry }: { error: unknown; onRetry: () => void }) => {
  if (isNotConnected(error)) return <NotConnected />;
  if (isNotConfigured(error)) return <NotConfigured />;
  return (
    <Banner
      type="critical"
      title="Could not load RecoverFlow data"
      description={errorDescription(error)}
      actions={<Button onPress={onRetry}>Retry</Button>}
    />
  );
};
