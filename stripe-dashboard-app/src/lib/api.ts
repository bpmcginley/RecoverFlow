import { fetchStripeSignature } from '@stripe/ui-extension-sdk/utils';
import type { ExtensionContextValue } from '@stripe/ui-extension-sdk/context';
import type {
  CaseListResponse,
  CaseSummary,
  CustomerCasesResponse,
  DunningResponse,
  RecoveryStatus,
  SummaryResponse,
} from './types';

const PRODUCTION_API_BASE = 'https://api.recoverflow.org/app/v1';

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;

  constructor(status: number, code: string, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
  }
}

export const isNotConnected = (error: unknown): boolean =>
  error instanceof ApiError && error.code === 'merchant_not_connected';

export const isNotConfigured = (error: unknown): boolean =>
  error instanceof ApiError && error.code === 'app_not_configured';

export const isCaseNotFound = (error: unknown): boolean =>
  error instanceof ApiError && error.code === 'case_not_found';

const apiBase = (context: ExtensionContextValue): string => {
  const constants = context.environment.constants;
  if (constants && typeof constants === 'object' && !Array.isArray(constants)) {
    const value = (constants as Record<string, unknown>)['API_BASE'];
    if (typeof value === 'string' && value.length > 0) return value;
  }
  return PRODUCTION_API_BASE;
};

type QueryParams = Record<string, string | number | undefined>;

const buildQuery = (params?: QueryParams): string => {
  if (!params) return '';
  const parts = Object.entries(params)
    .filter((entry): entry is [string, string | number] => entry[1] !== undefined)
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`);
  return parts.length > 0 ? `?${parts.join('&')}` : '';
};

// Every /app/v1 endpoint is POST. The JSON body is exactly the payload that
// fetchStripeSignature() signs: {"user_id":"...","account_id":"..."} in that
// key order, sent byte for byte as serialized here. Endpoint parameters go in
// the query string so the signed body stays constant.
async function post<T>(
  context: ExtensionContextValue,
  path: string,
  query?: QueryParams,
): Promise<T> {
  const userId = context.userContext.id;
  if (!userId) {
    throw new ApiError(0, 'missing_user_context', 'The Dashboard user id is not available.');
  }
  const body = JSON.stringify({
    user_id: userId,
    account_id: context.userContext.account.id,
  });
  const signature = await fetchStripeSignature();

  let response: Response;
  try {
    response = await fetch(`${apiBase(context)}${path}${buildQuery(query)}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Stripe-Signature': signature,
      },
      body,
    });
  } catch {
    throw new ApiError(0, 'network_error', 'Could not reach the RecoverFlow API.');
  }

  if (response.ok) {
    return (await response.json()) as T;
  }

  let code = 'unknown_error';
  let message = `The RecoverFlow API returned status ${response.status}.`;
  try {
    const parsed = (await response.json()) as { error?: string; message?: string };
    if (parsed.error) code = parsed.error;
    if (parsed.message) message = parsed.message;
  } catch {
    // Non-JSON error body; keep the defaults.
  }
  throw new ApiError(response.status, code, message);
}

export const getSummary = (context: ExtensionContextValue): Promise<SummaryResponse> =>
  post<SummaryResponse>(context, '/summary');

export interface CaseListOptions {
  page?: number;
  pageSize?: number;
  status?: RecoveryStatus;
}

export const getCases = (
  context: ExtensionContextValue,
  options: CaseListOptions = {},
): Promise<CaseListResponse> =>
  post<CaseListResponse>(context, '/cases', {
    page: options.page,
    pageSize: options.pageSize,
    status: options.status,
  });

export const getCustomerCases = (
  context: ExtensionContextValue,
  stripeCustomerId: string,
): Promise<CustomerCasesResponse> =>
  post<CustomerCasesResponse>(
    context,
    `/customers/${encodeURIComponent(stripeCustomerId)}/cases`,
  );

export const getInvoiceCase = (
  context: ExtensionContextValue,
  stripeInvoiceId: string,
): Promise<CaseSummary> =>
  post<CaseSummary>(context, `/invoices/${encodeURIComponent(stripeInvoiceId)}`);

export const getDunning = (
  context: ExtensionContextValue,
  caseId: string,
): Promise<DunningResponse> =>
  post<DunningResponse>(context, `/cases/${encodeURIComponent(caseId)}/dunning`);
