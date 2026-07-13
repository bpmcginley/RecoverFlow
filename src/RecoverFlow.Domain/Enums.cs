namespace RecoverFlow.Domain;

public enum DeclineType { HardDecline, SoftDecline, Unknown }

public enum RecoveryStatus { ActiveRecovery, Recovered, Lost, Cancelled }

public enum RecoveryMethod { SmartRetry, EmailSequence, CardUpdate, Manual, Unknown }

// Paid/Void tracking (via platform-account webhooks) is deliberately out of scope for v1;
// Sent means Stripe accepted and emailed the invoice.
public enum FeeInvoiceStatus { Pending, Sent, Failed }

// A one-off scan of the merchant's Stripe history, run right after they connect, so
// we can show "here's what you lost, here's what we'd have recovered" immediately.
public enum BacktestStatus { Pending, Complete, Failed }
