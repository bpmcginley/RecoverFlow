import { useEffect, useState } from 'react';

export interface AppDataState<T> {
  loading: boolean;
  data?: T;
  error?: unknown;
  reload: () => void;
}

// Small loader hook shared by every view: tracks loading, result, and error,
// ignores stale responses, and exposes a reload trigger for retry buttons.
export function useAppData<T>(
  load: () => Promise<T>,
  deps: ReadonlyArray<unknown>,
): AppDataState<T> {
  const [state, setState] = useState<{ loading: boolean; data?: T; error?: unknown }>({
    loading: true,
  });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setState({ loading: true });
    load().then(
      (data) => {
        if (!cancelled) setState({ loading: false, data });
      },
      (error) => {
        if (!cancelled) setState({ loading: false, error });
      },
    );
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, attempt]);

  return { ...state, reload: () => setAttempt((n) => n + 1) };
}
