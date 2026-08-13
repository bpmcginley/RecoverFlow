import type { DunningEmailType, RecoveryStatus } from './types';

// Summary totals are integer cents summed across currencies and are shown in
// USD format, matching the RecoverFlow web dashboard.
export const formatUsd = (cents: number): string =>
  new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(cents / 100);

// Per-case amounts carry their own lowercase ISO currency code.
export const formatAmount = (cents: number, currency: string): string => {
  try {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: currency.toUpperCase(),
    }).format(cents / 100);
  } catch {
    return `${(cents / 100).toFixed(2)} ${currency.toUpperCase()}`;
  }
};

export const formatPercent = (rate: number): string => `${(rate * 100).toFixed(1)}%`;

// US-style date formatting, per Stripe App review requirements.
export const formatDate = (iso: string): string =>
  new Date(iso).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });

export const statusLabel = (status: RecoveryStatus): string => {
  switch (status) {
    case 'ActiveRecovery':
      return 'Recovering';
    case 'Recovered':
      return 'Recovered';
    case 'Lost':
      return 'Lost';
    case 'Cancelled':
      return 'Cancelled';
  }
};

export const emailTypeLabel = (emailType: DunningEmailType): string => {
  switch (emailType) {
    case 'friendly_reminder':
      return 'Friendly reminder';
    case 'urgent':
      return 'Urgent notice';
    case 'final_notice':
      return 'Final notice';
    default:
      return emailType;
  }
};
