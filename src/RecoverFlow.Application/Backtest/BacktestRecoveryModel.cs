using RecoverFlow.Domain;
using RecoverFlow.Domain.Services;

namespace RecoverFlow.Application.Backtest;

/// <summary>
/// Conservative, deliberately-honest recovery-rate assumptions for the connect-time backtest.
/// These are ESTIMATES, presented to merchants as a low–high range, never a guarantee. Ranges
/// reflect what smart retries + dunning realistically win back per decline classification —
/// soft declines (insufficient funds, transient errors) recover well; hard declines (stolen,
/// fraudulent) essentially don't. Numbers err low so a real account beats its backtest.
/// </summary>
public static class BacktestRecoveryModel
{
    public static (double Low, double High) RecoveryRange(DeclineType type) => type switch
    {
        DeclineType.SoftDecline => (0.30, 0.50),
        DeclineType.Unknown => (0.15, 0.30),
        DeclineType.HardDecline => (0.00, 0.05),
        _ => (0.15, 0.30),
    };

    public static DeclineType Classify(string? declineCode) => DeclineCodeClassifier.Classify(declineCode);
}
