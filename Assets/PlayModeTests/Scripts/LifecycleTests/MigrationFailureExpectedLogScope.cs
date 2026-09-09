using System;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

// Expected API failures currently log their caught exception. Match only this
// case's precise timeout or cancellation; every other diagnostic stays visible.
public sealed class MigrationFailureExpectedLogScope : ILogHandler, IDisposable
{
    private readonly ILogHandler _previous;
    private readonly CancellationToken _token;
    private readonly bool _cancel;
    private readonly Func<bool> _phaseActive;
    private readonly string _label;
    public int count { get; private set; }

    public MigrationFailureExpectedLogScope(string label, bool cancel, CancellationToken token,
        Func<bool> phaseActive)
    {
        _label = label;
        _cancel = cancel;
        _token = token;
        _phaseActive = phaseActive;
        _previous = Debug.unityLogger.logHandler;
        Debug.unityLogger.logHandler = this;
    }

    public void LogException(Exception exception, Object context)
    {
        bool expected = _phaseActive() && (_cancel
            ? exception is OperationCanceledException cancelled && cancelled.CancellationToken == _token &&
              _token.IsCancellationRequested
            : exception is TimeoutException && exception.Message == "Host migration did not finish before its timeout.");
        if (expected)
        {
            count++;
            _previous.LogFormat(LogType.Log, context,
                "[MigrationFailureExpected] {0}: observed expected bounded API outcome", new object[] { _label });
            return;
        }
        _previous.LogException(exception, context);
    }

    public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
        _previous.LogFormat(logType, context, format, args);

    public void Dispose() => Debug.unityLogger.logHandler = _previous;
}
