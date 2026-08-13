// Response shapes for the RecoverFlow app API (/app/v1).
// Every field maps 1:1 to the records serialized by RecoverFlow.Api with
// ASP.NET Core default JSON options: camelCase properties, PascalCase
// dictionary keys, enums as strings, DateTime as ISO 8601 UTC.

export type RecoveryStatus = 'ActiveRecovery' | 'Recovered' | 'Lost' | 'Cancelled';

// Dictionary keys stay PascalCase (System.Text.Json camelCases properties
// but not dictionary keys), so casesByStatus is keyed by the enum names.
export interface Stats {
  casesByStatus: Partial<Record<RecoveryStatus, number>>;
  amountRecoveredCents: number;
  amountAtRiskCents: number;
  recoveryRate: number;
  amountRecoveredGrossCents: number;
  amountReversedCents: number;
}

export interface SummaryResponse {
  allTime: Stats;
  last30Days: Stats;
}

export interface CaseSummary {
  id: string;
  stripeInvoiceId: string;
  customerEmail: string | null;
  amountCents: number;
  currency: string;
  status: RecoveryStatus;
  openedAt: string;
  closedAt: string | null;
  attemptCount: number;
  reversedCents: number;
  reversalReason: 'refund' | 'dispute' | null;
}

export interface CaseListResponse {
  items: CaseSummary[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface CustomerCasesResponse {
  items: CaseSummary[];
  totalCount: number;
}

export type DunningEmailType = 'friendly_reminder' | 'urgent' | 'final_notice';

export interface DunningEntry {
  sequenceStep: number;
  emailType: DunningEmailType;
  sentAt: string;
  openedAt: string | null;
  clickedAt: string | null;
  resultedInRecovery: boolean;
}

export interface DunningResponse {
  caseId: string;
  entries: DunningEntry[];
}
