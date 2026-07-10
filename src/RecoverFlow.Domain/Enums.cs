namespace RecoverFlow.Domain;

public enum DeclineType { HardDecline, SoftDecline, Unknown }

public enum RecoveryStatus { ActiveRecovery, Recovered, Lost, Cancelled }

public enum RecoveryMethod { SmartRetry, EmailSequence, CardUpdate, Manual, Unknown }
