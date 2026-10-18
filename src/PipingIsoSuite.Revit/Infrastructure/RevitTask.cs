using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace PipingIsoSuite.Revit.Infrastructure;

/// <summary>
/// Marshals work from the modeless WPF pane into a valid Revit API context via one ExternalEvent.
/// Must be constructed inside an API context (e.g. OnStartup).
/// </summary>
public sealed class RevitTask : IExternalEventHandler
{
    private readonly ConcurrentQueue<Action<UIApplication>> _queue = new();
    private readonly ExternalEvent _event;

    public RevitTask() => _event = ExternalEvent.Create(this);

    public Task<T> Run<T>(Func<UIApplication, T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        _queue.Enqueue(app =>
        {
            try { tcs.SetResult(func(app)); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        _event.Raise();
        return tcs.Task;
    }

    public void Execute(UIApplication app)
    {
        while (_queue.TryDequeue(out var work)) work(app);
    }

    public string GetName() => "Piping ISO Suite";
}
